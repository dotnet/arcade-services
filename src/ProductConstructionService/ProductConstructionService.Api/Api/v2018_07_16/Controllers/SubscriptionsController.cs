// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.v2018_07_16.Models;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Api.v2018_07_16.Controllers;

/// <summary>
///   Exposes methods to Read <see cref="Subscription"/>s
/// </summary>
[Route("subscriptions")]
[ApiVersion("2018-07-16")]
public class SubscriptionsController : ControllerBase
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;
    private readonly ILogger<SubscriptionsController> _logger;

    public SubscriptionsController(
        BuildAssetRegistryContext context,
        IWorkItemProducerFactory workItemProducerFactory,
        ILogger<SubscriptionsController> logger)
    {
        _context = context;
        _workItemProducerFactory = workItemProducerFactory;
        _logger = logger;
    }

    /// <summary>
    ///   Gets a list of all <see cref="Subscription"/>s that match the given search criteria.
    /// </summary>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Subscription>), Description = "The list of Subscriptions")]
    [ValidateModelState]
    public virtual IActionResult ListSubscriptions(
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
    public virtual async Task<IActionResult> GetSubscription(Guid id)
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

    /// <summary>
    ///   Trigger a <see cref="Subscription"/> manually by id
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/> to trigger.</param>
    /// <param name="buildId">'bar-build-id' if specified, a specific build is requested</param>
    /// <param name="force">'force' if specified, force update even for PRs with pending or successful checks</param>
    [HttpPost("{id}/trigger")]
    [SwaggerApiResponse(HttpStatusCode.Accepted, Type = typeof(Subscription), Description = "Subscription update has been triggered")]
    [ValidateModelState]
    public virtual async Task<IActionResult> TriggerSubscription(Guid id, [FromQuery(Name = "bar-build-id")] int buildId = 0, [FromQuery(Name = "force")] bool force = false)
    {
        return await TriggerSubscriptionCore(id, buildId, force);
    }

    protected async Task<IActionResult> TriggerSubscriptionCore(Guid id, int buildId, bool force = false)
    {
        Maestro.Data.Models.Subscription? subscription = await _context.Subscriptions
            .Include(sub => sub.LastAppliedBuild)
            .Include(sub => sub.Channel)
            .FirstOrDefaultAsync(sub => sub.Id == id);

        if (buildId != 0)
        {
            Maestro.Data.Models.Build? build = await _context.Builds.Where(b => b.Id == buildId).FirstOrDefaultAsync();
            // Non-existent build
            if (build == null)
            {
                return BadRequest(new ApiError($"Build {buildId} was not found"));
            }
            // Build doesn't match source repo
            if (!(build.GitHubRepository?.Equals(subscription?.SourceRepository, StringComparison.InvariantCultureIgnoreCase) == true ||
                  build.AzureDevOpsRepository?.Equals(subscription?.SourceRepository, StringComparison.InvariantCultureIgnoreCase) == true))
            {
                return BadRequest(new ApiError($"Build {buildId} does not match source repo"));
            }
        }

        if (subscription == null)
        {
            return NotFound();
        }

        if (subscription.Enabled == false)
        {
            return BadRequest(new ApiError("Subscription is disabled"));
        }

        await EnqueueUpdateSubscriptionWorkItemAsync(id, buildId, force);

        return Accepted(new Subscription(subscription));
    }

    private async Task EnqueueUpdateSubscriptionWorkItemAsync(Guid subscriptionId, int buildId, bool force = false)
    {
        Maestro.Data.Models.Subscription? subscriptionToUpdate;
        if (buildId != 0)
        {
            // Update using a specific build
            subscriptionToUpdate =
                (from sub in _context.Subscriptions
                 where sub.Id == subscriptionId
                 let specificBuild =
                     sub.Channel.BuildChannels.Select(bc => bc.Build)
                         .Where(b => sub.SourceRepository == b.GitHubRepository || sub.SourceRepository == b.AzureDevOpsRepository)
                         .Where(b => b.Id == buildId)
                         .FirstOrDefault()
                 where specificBuild != null
                 select sub).SingleOrDefault();
        }
        else
        {
            // Update using the latest build
            var subscriptionAndBuild =
                (from sub in _context.Subscriptions
                 where sub.Id == subscriptionId
                 let latestBuild =
                     sub.Channel.BuildChannels.Select(bc => bc.Build)
                         .Where(b => sub.SourceRepository == b.GitHubRepository || sub.SourceRepository == b.AzureDevOpsRepository)
                         .OrderByDescending(b => b.DateProduced)
                         .FirstOrDefault()
                 where latestBuild != null
                 select new
                 {
                     subscription = sub,
                     latestBuildId = latestBuild.Id
                 }).SingleOrDefault();
            subscriptionToUpdate = subscriptionAndBuild?.subscription;
            buildId = subscriptionAndBuild?.latestBuildId ?? 0;
        }

        if (subscriptionToUpdate != null)
        {
            _logger.LogInformation("Will trigger {subscriptionId} with build {buildId}", subscriptionId, buildId);

            await _workItemProducerFactory.CreateProducer<SubscriptionTriggerWorkItem>(subscriptionToUpdate.SourceEnabled).ProduceWorkItemAsync(new()
            {
                SubscriptionId = subscriptionToUpdate.Id,
                BuildId = buildId,
                Force = force
            });
        }
        else if (buildId != 0)
        {
            _logger.LogInformation("Suitable build {buildId} was not found in channel matching subscription {subscriptionId}. Not triggering updates", buildId, subscriptionId);
        }
        else
        {
            _logger.LogWarning("No suitable build was found in channel matching subscription {subscriptionId}. Not triggering updates", subscriptionId);
        }
    }

    /// <summary>
    ///   Trigger daily update
    /// </summary>
    [HttpPost("triggerDaily")]
    [SwaggerApiResponse(HttpStatusCode.Accepted, Description = "Trigger all subscriptions normally updated daily.")]
    [ValidateModelState]
    public virtual async Task<IActionResult> TriggerDailyUpdateAsync()
    {
        // TODO put this and the code in SubscriptionTriggerer in the same place to avoid dupplication
        var enabledSubscriptionsWithTargetFrequency = (await _context.Subscriptions
                .Where(s => s.Enabled)
                .ToListAsync())
                .Where(s => (int)s.PolicyObject.UpdateFrequency == (int)UpdateFrequency.EveryDay);

        var workitemProducer = _workItemProducerFactory.CreateProducer<SubscriptionTriggerWorkItem>();

        foreach (var subscription in enabledSubscriptionsWithTargetFrequency)
        {
            Maestro.Data.Models.Subscription? subscriptionWithBuilds = await _context.Subscriptions
                .Where(s => s.Id == subscription.Id)
                .Include(s => s.Channel)
                .ThenInclude(c => c.BuildChannels)
                .ThenInclude(bc => bc.Build)
                .FirstOrDefaultAsync();

            if (subscriptionWithBuilds == null)
            {
                _logger.LogWarning("Subscription {subscriptionId} was not found in the BAR. Not triggering updates", subscription.Id.ToString());
                continue;
            }

            Maestro.Data.Models.Build? latestBuildInTargetChannel = subscriptionWithBuilds.Channel.BuildChannels.Select(bc => bc.Build)
                .Where(b => (subscription.SourceRepository == b.GitHubRepository || subscription.SourceRepository == b.AzureDevOpsRepository))
                .OrderByDescending(b => b.DateProduced)
                .FirstOrDefault();

            bool isThereAnUnappliedBuildInTargetChannel = latestBuildInTargetChannel != null &&
                (subscription.LastAppliedBuild == null || subscription.LastAppliedBuildId != latestBuildInTargetChannel.Id);

            if (isThereAnUnappliedBuildInTargetChannel && latestBuildInTargetChannel != null)
            {
                _logger.LogInformation("Will trigger {subscriptionId} to build {latestBuildInTargetChannelId}", subscription.Id, latestBuildInTargetChannel.Id);
                await workitemProducer.ProduceWorkItemAsync(new()
                {
                    SubscriptionId = subscription.Id,
                    BuildId = latestBuildInTargetChannel.Id
                });
            }
        }

        return Accepted();
    }

    /// <summary>
    ///   Gets a paginated list of the Subscription history for the given Subscription
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/> to get history for</param>
    [HttpGet("{id}/history")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<SubscriptionHistoryItem>), Description = "The list of Subscription history")]
    [Paginated(typeof(SubscriptionHistoryItem))]
    public virtual async Task<IActionResult> GetSubscriptionHistory(Guid id)
    {
        Maestro.Data.Models.Subscription? subscription = await _context.Subscriptions.Where(sub => sub.Id == id)
            .FirstOrDefaultAsync();

        if (subscription == null)
        {
            return NotFound();
        }

        IOrderedQueryable<SubscriptionUpdateHistoryEntry> query = _context.SubscriptionUpdateHistory
            .Where(u => u.SubscriptionId == id)
            .OrderByDescending(u => u.Timestamp);

        return Ok(query);
    }
}
