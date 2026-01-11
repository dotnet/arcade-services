// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProductConstructionService.Api.Controllers.Models;
using ProductConstructionService.Api.v2019_01_16.Models;
using ProductConstructionService.Common.CodeflowHistory;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Api.v2019_01_16.Controllers;

/// <summary>
///   Exposes methods to Create/Read/Update/Delete <see cref="Subscription"/>s
/// </summary>
[Route("subscriptions")]
[ApiVersion("2019-01-16")]
public class SubscriptionsController : v2018_07_16.Controllers.SubscriptionsController
{
    private readonly BuildAssetRegistryContext _context;

    public SubscriptionsController(
        BuildAssetRegistryContext context,
        IWorkItemProducerFactory workItemProducerFactory,
        IGitHubInstallationIdResolver gitHubInstallationRetriever,
        IRemoteFactory remoteFactory,
        ICodeflowHistoryManager codeflowHistoryManager,
        IOptions<EnvironmentNamespaceOptions> environmentNamespaceOptions,
        ILogger<SubscriptionsController> logger)
        : base(context, workItemProducerFactory, gitHubInstallationRetriever, environmentNamespaceOptions, remoteFactory, codeflowHistoryManager, logger)
    {
        _context = context;
    }

    /// <summary>
    ///   Gets a list of all <see cref="Subscription"/>s that match the given search criteria.
    /// </summary>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Subscription>), Description = "The list of Subscriptions")]
    [ValidateModelState]
    public override IActionResult ListSubscriptions(
        string? sourceRepository = null,
        string? targetRepository = null,
        int? channelId = null,
        bool? enabled = null)
    {
        IQueryable<Maestro.Data.Models.Subscription> query = _context.Subscriptions.Include(s => s.Channel);

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
            .FirstOrDefaultAsync(sub => sub.Id == id);

        if (subscription == null)
        {
            return NotFound();
        }

        return Ok(new Subscription(subscription));
    }
    /*
    [HttpGet("{id}/codeflowhistory")]
    [SwaggerApiResponse(HttpStatusCode.Accepted, Type = typeof(CodeflowHistoryResult), Description = "The codeflow history")]
    [ValidateModelState]
    public override async Task<IActionResult> GetCodeflowHistory(Guid id)
    {
        return await GetCodeflowHistoryCore(id);
    }
    */

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

    /// <summary>
    ///   Edit an existing <see cref="Subscription"/>
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/> to update</param>
    /// <param name="update">An object containing the new data for the <see cref="Subscription"/></param>
    [HttpPatch("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Subscription), Description = "Subscription successfully updated")]
    [ValidateModelState]
    public override async Task<IActionResult> UpdateSubscription(Guid id, [FromBody] ProductConstructionService.Api.v2018_07_16.Models.SubscriptionUpdate update)
    {
        Maestro.Data.Models.Subscription? subscription = await _context.Subscriptions.Where(sub => sub.Id == id)
            .FirstOrDefaultAsync();

        if (subscription == null)
        {
            return NotFound();
        }

        var doUpdate = false;

        if (!string.IsNullOrEmpty(update.SourceRepository))
        {
            subscription.SourceRepository = update.SourceRepository;
            doUpdate = true;
        }

        if (update.Policy != null)
        {
            subscription.PolicyObject = update.Policy.ToDb();
            doUpdate = true;
        }

        if (!string.IsNullOrEmpty(update.ChannelName))
        {
            Maestro.Data.Models.Channel? channel = await _context.Channels.Where(c => c.Name == update.ChannelName)
                .FirstOrDefaultAsync();
            if (channel == null)
            {
                return BadRequest(
                    new ApiError(
                        "The request is invalid",
                        new[] { $"The channel '{update.ChannelName}' could not be found." }));
            }

            subscription.Channel = channel;
            doUpdate = true;
        }

        if (update.Enabled.HasValue)
        {
            subscription.Enabled = update.Enabled.Value;
            doUpdate = true;
        }

        if (doUpdate)
        {
            Maestro.Data.Models.Subscription? equivalentSubscription = await FindEquivalentSubscription(subscription);
            if (equivalentSubscription != null)
            {
                return Conflict(
                    new ApiError(
                        "the request is invalid",
                        new[]
                        {
                            $"The subscription '{equivalentSubscription.Id}' already performs the same update."
                        }));
            }

            _context.Subscriptions.Update(subscription);
            await _context.SaveChangesAsync();
        }


        return Ok(new Subscription(subscription));
    }

    /// <summary>
    ///   Delete an existing <see cref="Subscription"/>
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/> to delete</param>
    [HttpDelete("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Subscription), Description = "Subscription successfully deleted")]
    [ValidateModelState]
    public override async Task<IActionResult> DeleteSubscription(Guid id)
    {
        Maestro.Data.Models.Subscription? subscription =
            await _context.Subscriptions.FirstOrDefaultAsync(sub => sub.Id == id);

        if (subscription == null)
        {
            return NotFound();
        }

        Maestro.Data.Models.SubscriptionUpdate? subscriptionUpdate =
            await _context.SubscriptionUpdates.FirstOrDefaultAsync(u => u.SubscriptionId == subscription.Id);

        if (subscriptionUpdate != null)
        {
            _context.SubscriptionUpdates.Remove(subscriptionUpdate);
        }

        _context.Subscriptions.Remove(subscription);

        await _context.SaveChangesAsync();
        return Ok(new Subscription(subscription));
    }

    /// <summary>
    ///   Creates a new <see cref="Subscription"/>
    /// </summary>
    /// <param name="subscription">An object containing data for the new <see cref="Subscription"/></param>
    [HttpPost]
    [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Subscription), Description = "New Subscription successfully created")]
    [ValidateModelState]
    public override async Task<IActionResult> Create([FromBody, Required] ProductConstructionService.Api.v2018_07_16.Models.SubscriptionData subscription)
    {
        Maestro.Data.Models.Channel? channel = await _context.Channels.Where(c => c.Name == subscription.ChannelName)
            .FirstOrDefaultAsync();
        if (channel == null)
        {
            return BadRequest(
                new ApiError(
                    "the request is invalid",
                    new[] { $"The channel '{subscription.ChannelName}' could not be found." }));
        }

        if (!await EnsureRepositoryRegistration(subscription.TargetRepository))
        {
            return BadRequest(new ApiError("The request is invalid",
            [
                $"No Maestro GitHub application installation found for repository '{subscription.TargetRepository}'. " +
                "The Maestro github application must be installed by the repository's owner and given access to the repository."
            ]));
        }

        var defaultNamespace = await _context.Namespaces.SingleAsync(n => n.Name == _environmentNamespaceOptions.Value.DefaultNamespaceName);
        Maestro.Data.Models.Subscription subscriptionModel = subscription.ToDb();
        subscriptionModel.Channel = channel;
        subscriptionModel.Id = Guid.NewGuid();
        subscriptionModel.Namespace = defaultNamespace;

        // Check that we're not about add an existing subscription that is identical
        Maestro.Data.Models.Subscription? equivalentSubscription = await FindEquivalentSubscription(subscriptionModel);
        if (equivalentSubscription != null)
        {
            return BadRequest(
                new ApiError(
                    "the request is invalid",
                    new[]
                    {
                        $"The subscription '{equivalentSubscription.Id}' already performs the same update."
                    }));
        }

        await _context.Subscriptions.AddAsync(subscriptionModel);
        await _context.SaveChangesAsync();
        return CreatedAtRoute(
            new
            {
                action = "GetSubscription",
                id = subscriptionModel.Id
            },
            new Subscription(subscriptionModel));
    }

    /// <summary>
    ///     Find an existing subscription in the database with the same key data as the subscription we are adding/updating
    ///     
    ///     This should be called before updating or adding new subscriptions to the database
    /// </summary>
    /// <param name="updatedOrNewSubscription">Subscription model with updated data.</param>
    /// <returns>Subscription if it is found, null otherwise</returns>
    private async Task<Maestro.Data.Models.Subscription?> FindEquivalentSubscription(Maestro.Data.Models.Subscription updatedOrNewSubscription)
    {
        // Compare subscriptions based on the 4 key elements:
        // - Channel
        // - Source repo
        // - Target repo
        // - Target branch
        // - Not the same subscription id (for updates)
        return await _context.Subscriptions.FirstOrDefaultAsync(sub =>
            sub.SourceRepository == updatedOrNewSubscription.SourceRepository &&
            sub.ChannelId == updatedOrNewSubscription.Channel.Id &&
            sub.TargetRepository == updatedOrNewSubscription.TargetRepository &&
            sub.TargetBranch == updatedOrNewSubscription.TargetBranch &&
            sub.Id != updatedOrNewSubscription.Id);
    }
}
