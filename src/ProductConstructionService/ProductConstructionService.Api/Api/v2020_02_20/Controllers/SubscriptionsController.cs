// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.v2020_02_20.Models;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

/// <summary>
///   Exposes methods to Read <see cref="Subscription"/>s
/// </summary>
[Route("subscriptions")]
[ApiVersion("2020-02-20")]
public class SubscriptionsController : v2019_01_16.Controllers.SubscriptionsController
{
    private readonly BuildAssetRegistryContext _context;

    public SubscriptionsController(
        BuildAssetRegistryContext context,
        IWorkItemProducerFactory workItemProducerFactory,
        ILogger<SubscriptionsController> logger)
        : base(context, workItemProducerFactory, logger)
    {
        _context = context;
    }

    [ApiRemoved]
    public sealed override IActionResult ListSubscriptions(
        string? sourceRepository = null,
        string? targetRepository = null,
        int? channelId = null,
        bool? enabled = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///   Gets a list of all <see cref="Subscription"/>s that match the given search criteria.
    /// </summary>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Subscription>), Description = "The list of Subscriptions")]
    [ValidateModelState]
    public IActionResult ListSubscriptions(
        string? sourceRepository = null,
        string? targetRepository = null,
        int? channelId = null,
        bool? enabled = null,
        bool? sourceEnabled = null,
        string? sourceDirectory = null,
        string? targetDirectory = null)
    {
        IQueryable<Maestro.Data.Models.Subscription> query = _context.Subscriptions
            .Include(s => s.Channel)
            .Include(s => s.LastAppliedBuild)
            .Include(s => s.ExcludedAssets);

        if (!string.IsNullOrEmpty(sourceRepository))
        {
            query = query.Where(sub => sub.SourceRepository == sourceRepository);
        }

        if (!string.IsNullOrEmpty(targetRepository))
        {
            query = query.Where(sub => sub.TargetRepository == targetRepository);
        }

        if (channelId.HasValue)
        {
            query = query.Where(sub => sub.ChannelId == channelId.Value);
        }

        if (enabled.HasValue)
        {
            query = query.Where(sub => sub.Enabled == enabled.Value);
        }

        if (sourceEnabled.HasValue)
        {
            query = query.Where(sub => sub.SourceEnabled == sourceEnabled.Value);
        }

        if (!string.IsNullOrEmpty(sourceDirectory))
        {
            query = query.Where(sub => sub.SourceDirectory == sourceDirectory);
        }

        if (!string.IsNullOrEmpty(targetDirectory))
        {
            query = query.Where(sub => sub.TargetDirectory == targetDirectory);
        }

        List<Subscription> results = [.. query.AsEnumerable().Select(sub => new Subscription(sub))];
        return Ok(results);
    }

    /// <summary>
    ///   Gets a single <see cref="Subscription"/>
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/></param>
    [HttpGet("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Subscription), Description = "The requested Subscription")]
    [ValidateModelState]
    public override async Task<IActionResult> GetSubscription(Guid id)
    {
        Maestro.Data.Models.Subscription? subscription = await _context.Subscriptions.Include(sub => sub.LastAppliedBuild)
            .Include(sub => sub.Channel)
            .Include(sub => sub.LastAppliedBuild)
            .Include(sub => sub.ExcludedAssets)
            .FirstOrDefaultAsync(sub => sub.Id == id);

        if (subscription == null)
        {
            return NotFound();
        }

        return Ok(new Subscription(subscription));
    }

    /// <summary>
    ///   Trigger a <see cref="Subscription"/> manually by id
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/> to trigger.</param>
    /// <param name="buildId">'bar-build-id' if specified, a specific build is requested</param>
    /// <param name="force">'force' if specified, force update even for PRs with pending or successful checks</param>
    [HttpPost("{id}/trigger")]
    [SwaggerApiResponse(HttpStatusCode.Accepted, Type = typeof(Subscription), Description = "Subscription update has been triggered")]
    [ValidateModelState]
    public override async Task<IActionResult> TriggerSubscription(Guid id, [FromQuery(Name = "bar-build-id")] int buildId = 0, [FromQuery(Name = "force")] bool force = false)
    {
        return await TriggerSubscriptionCore(id, buildId, force);
    }

}
