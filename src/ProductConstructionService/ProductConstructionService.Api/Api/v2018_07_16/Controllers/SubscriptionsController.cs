// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Net;
using Maestro.Api.Model.v2018_07_16;
using Maestro.Data;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

using Channel = Maestro.Data.Models.Channel;

namespace ProductConstructionService.Api.Api.v2018_07_16.Controllers;

/// <summary>
///   Exposes methods to Create/Read/Update/Delete <see cref="Subscription"/>s
/// </summary>
[Route("subscriptions")]
[ApiVersion("2018-07-16")]
public class SubscriptionsController : ControllerBase
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;
    private readonly ILogger<SubscriptionsController> _logger;
    // TODO (https://github.com/dotnet/arcade-services/issues/3880) - Remove subscriptionIdGenerator
    protected readonly SubscriptionIdGenerator _subscriptionIdGenerator;

    public SubscriptionsController(
        BuildAssetRegistryContext context,
        IWorkItemProducerFactory workItemProducerFactory,
        ILogger<SubscriptionsController> logger,
        SubscriptionIdGenerator subscriptionIdGenerator)
    {
        _context = context;
        _workItemProducerFactory = workItemProducerFactory;
        _logger = logger;
        _subscriptionIdGenerator = subscriptionIdGenerator;
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

        List<Subscription> results = query.AsEnumerable().Select(sub => new Subscription(sub)).ToList();
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
    [HttpPost("{id}/trigger")]
    [SwaggerApiResponse(HttpStatusCode.Accepted, Type = typeof(Subscription), Description = "Subscription update has been triggered")]
    [ValidateModelState]
    public virtual async Task<IActionResult> TriggerSubscription(Guid id, [FromQuery(Name = "bar-build-id")] int buildId = 0)
    {
        return await TriggerSubscriptionCore(id, buildId);
    }

    protected async Task<IActionResult> TriggerSubscriptionCore(Guid id, int buildId)
    {
        // TODO (https://github.com/dotnet/arcade-services/issues/3880) - Remove subscriptionIdGenerator
        if (!_subscriptionIdGenerator.ShouldTriggerSubscription(id))
        {
            return BadRequest(
                new ApiError($"PCS can only trigger subscriptions which ids start with ${SubscriptionIdGenerator.PcsSubscriptionIdPrefix}")
            );
        }
        Maestro.Data.Models.Subscription? subscription = await _context.Subscriptions.Include(sub => sub.LastAppliedBuild)
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

        await EnqueueUpdateSubscriptionWorkItemAsync(id, buildId);

        return Accepted(new Subscription(subscription));
    }

    private async Task EnqueueUpdateSubscriptionWorkItemAsync(Guid subscriptionId, int buildId)
    {
        Maestro.Data.Models.Subscription? subscriptionToUpdate;
        if (buildId != 0)
        {
            // Update using a specific build
            subscriptionToUpdate =
                (from sub in _context.Subscriptions
                 where sub.Id == subscriptionId
                 where sub.Enabled
                 let specificBuild =
                     sub.Channel.BuildChannels.Select(bc => bc.Build)
                         .Where(b => (sub.SourceRepository == b.GitHubRepository || sub.SourceRepository == b.AzureDevOpsRepository))
                         .Where(b => b.Id == buildId)
                         .FirstOrDefault()
                 where specificBuild != null
                 select sub).SingleOrDefault();
        }
        else
        {
            // Update using the latest build
            subscriptionToUpdate =
                (from sub in _context.Subscriptions
                 where sub.Id == subscriptionId
                 where sub.Enabled
                 let latestBuild =
                     sub.Channel.BuildChannels.Select(bc => bc.Build)
                         .Where(b => (sub.SourceRepository == b.GitHubRepository || sub.SourceRepository == b.AzureDevOpsRepository))
                         .OrderByDescending(b => b.DateProduced)
                         .FirstOrDefault()
                 where latestBuild != null
                 select sub).SingleOrDefault();
        }

        if (subscriptionToUpdate != null)
        {
            await _workItemProducerFactory.CreateProducer<SubscriptionTriggerWorkItem>(subscriptionToUpdate.SourceEnabled).ProduceWorkItemAsync(new()
            {
                SubscriptionId = subscriptionToUpdate.Id,
                BuildId = buildId
            });
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
    ///   Edit an existing <see cref="Subscription"/>
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/> to update</param>
    /// <param name="update">An object containing the new data for the <see cref="Subscription"/></param>
    [HttpPatch("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Subscription), Description = "Subscription successfully updated")]
    [ValidateModelState]
    public virtual async Task<IActionResult> UpdateSubscription(Guid id, [FromBody] SubscriptionUpdate update)
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
            Channel? channel = await _context.Channels.Where(c => c.Name == update.ChannelName)
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
                return BadRequest(
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
    public virtual async Task<IActionResult> DeleteSubscription(Guid id)
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

    /// <summary>
    ///   Creates a new <see cref="Subscription"/>
    /// </summary>
    /// <param name="subscription">An object containing data for the new <see cref="Subscription"/></param>
    [HttpPost]
    [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Subscription), Description = "New Subscription successfully created")]
    [ValidateModelState]
    public virtual async Task<IActionResult> Create([FromBody, Required] SubscriptionData subscription)
    {
        Channel? channel = await _context.Channels.Where(c => c.Name == subscription.ChannelName)
            .FirstOrDefaultAsync();
        if (channel == null)
        {
            return BadRequest(
                new ApiError(
                    "the request is invalid",
                    new[] { $"The channel '{subscription.ChannelName}' could not be found." }));
        }

        Maestro.Data.Models.Repository? repo = await _context.Repositories.FindAsync(subscription.TargetRepository);

        if (subscription.TargetRepository.Contains("github.com"))
        {
            // If we have no repository information or an invalid installation id
            // then we will fail when trying to update things, so we fail early.
            if (repo == null || repo.InstallationId <= 0)
            {
                return BadRequest(
                    new ApiError(
                        "the request is invalid",
                        new[]
                        {
                            $"The repository '{subscription.TargetRepository}' does not have an associated github installation. " +
                            "The Maestro github application must be installed by the repository's owner and given access to the repository."
                        }));
            }
        }
        // In the case of a dev.azure.com repository, we don't have an app installation,
        // but we should add an entry in the repositories table, as this is required when
        // adding a new subscription policy.
        // NOTE:
        // There is a good chance here that we will need to also handle <account>.visualstudio.com
        // but leaving it out for now as it would be preferred to use the new format
        else if (subscription.TargetRepository.Contains("dev.azure.com"))
        {
            if (repo == null)
            {
                _context.Repositories.Add(
                    new Maestro.Data.Models.Repository
                    {
                        RepositoryName = subscription.TargetRepository,
                        InstallationId = default
                    });
            }
        }

        Maestro.Data.Models.Subscription subscriptionModel = subscription.ToDb();
        subscriptionModel.Channel = channel;
        subscriptionModel.Id = _subscriptionIdGenerator.GenerateSubscriptionId();

        Maestro.Data.Models.Subscription? equivalentSubscription = await FindEquivalentSubscription(subscriptionModel);
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
