// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Asset = Maestro.Contracts.Asset;

namespace ProductConstructionService.DependencyFlow;

internal class SubscriptionTriggerer : ISubscriptionTriggerer
{
    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly BuildAssetRegistryContext _context;
    private readonly ILogger<SubscriptionTriggerer> _logger;
    private readonly Guid _subscriptionId;

    public SubscriptionTriggerer(
        BuildAssetRegistryContext context,
        IPullRequestUpdaterFactory updaterFactory,
        ILogger<SubscriptionTriggerer> logger,
        Guid subscriptionId)
    {
        _context = context;
        _updaterFactory = updaterFactory;
        _logger = logger;
        _subscriptionId = subscriptionId;
    }

    public async Task<bool> UpdateForMergedPullRequestAsync(int updateBuildId)
    {
        _logger.LogInformation("Updating {subscriptionId} with latest build id {buildId}", _subscriptionId, updateBuildId);
        Subscription? subscription = await _context.Subscriptions.FindAsync(_subscriptionId);

        if (subscription != null)
        {
            subscription.LastAppliedBuildId = updateBuildId;
            _context.Subscriptions.Update(subscription);
            await _context.SaveChangesAsync();
            return true;
        }
        else
        {
            _logger.LogInformation("Could not find subscription with ID {subscriptionId}. Skipping latestBuild update.", _subscriptionId);
            return false;
        }
    }

    public async Task<bool> AddDependencyFlowEventAsync(
        int updateBuildId,
        DependencyFlowEventType flowEvent,
        DependencyFlowEventReason reason,
        MergePolicyCheckResult policy,
        string flowType,
        string? url)
    {
        var updateReason = reason == DependencyFlowEventReason.New || reason == DependencyFlowEventReason.AutomaticallyMerged
            ? reason.ToString()
            : $"{reason}{policy}";

        _logger.LogInformation(
            "Adding dependency flow event for {subscriptionId} with {flowEvent} {updateReason} {flowType}",
            _subscriptionId,
            flowEvent,
            updateReason,
            flowType);

        Subscription? subscription = await _context.Subscriptions.FindAsync(_subscriptionId);
        if (subscription != null)
        {
            var dfe = new DependencyFlowEvent
            {
                SourceRepository = subscription.SourceRepository,
                TargetRepository = subscription.TargetRepository,
                ChannelId = subscription.ChannelId,
                BuildId = updateBuildId,
                Timestamp = DateTimeOffset.UtcNow,
                Event = flowEvent.ToString(),
                Reason = updateReason,
                FlowType = flowType,
                Url = url,
            };
            _context.DependencyFlowEvents.Add(dfe);
            await _context.SaveChangesAsync();
            return true;
        }
        else
        {
            _logger.LogInformation("Could not find subscription with ID {subscriptionId}. Skipping adding dependency flow event.", _subscriptionId);
            return false;
        }
    }

    public async Task UpdateSubscriptionAsync(int buildId)
    {
        Subscription? subscription = await _context.Subscriptions.FindAsync(_subscriptionId);

        if (subscription == null)
        {
            _logger.LogWarning("Could not find subscription with ID {subscriptionId}. Skipping update.", _subscriptionId);
            return;
        }

        await AddDependencyFlowEventAsync(
            buildId,
            DependencyFlowEventType.Fired,
            DependencyFlowEventReason.New,
            MergePolicyCheckResult.PendingPolicies,
            "PR",
            null);

        _logger.LogInformation("Looking up build {buildId}", buildId);

        Build build = await _context.Builds.Include(b => b.Assets)
            .ThenInclude(a => a.Locations)
            .FirstAsync(b => b.Id == buildId);

        IPullRequestUpdater pullRequestActor;

        if (subscription.PolicyObject.Batchable)
        {
            _logger.LogInformation("Creating pull request updater for branch {branch} of {repository}",
                subscription.TargetBranch,
                subscription.TargetRepository);

            pullRequestActor = _updaterFactory.CreatePullRequestUpdater(
                new BatchedPullRequestActorId(subscription.TargetRepository, subscription.TargetBranch));
        }
        else
        {
            _logger.LogInformation("Creating pull request updater for subscription {subscriptionId}",
                _subscriptionId);

            pullRequestActor = _updaterFactory.CreatePullRequestUpdater(
                new NonBatchedPullRequestUpdaterId(_subscriptionId));
        }

        var assets = build.Assets
            .Select(a => new Asset
            {
                Name = a.Name,
                Version = a.Version
            })
            .ToList();

        _logger.LogInformation("Running asset update for {subscriptionId}", _subscriptionId);

        await pullRequestActor.UpdateAssetsAsync(
            _subscriptionId,
            subscription.SourceEnabled
                ? SubscriptionType.DependenciesAndSources
                : SubscriptionType.Dependencies,
            build.Id,
            build.GitHubRepository ?? build.AzureDevOpsRepository,
            build.Commit,
            assets);

        _logger.LogInformation("Asset update complete for {subscriptionId}", _subscriptionId);
    }
}
