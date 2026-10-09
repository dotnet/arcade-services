// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Services.Common.Cache;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;

namespace ProductConstructionService.DependencyFlow;

internal class SubscriptionTriggerer : ISubscriptionTriggerer
{
    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly BuildAssetRegistryContext _context;
    private readonly ILogger<SubscriptionTriggerer> _logger;
    private readonly IDistributedLock _distributedLock;
    private readonly Guid _subscriptionId;

    public SubscriptionTriggerer(
        BuildAssetRegistryContext context,
        IPullRequestUpdaterFactory updaterFactory,
        ILogger<SubscriptionTriggerer> logger,
        IDistributedLock distributedLock,
        Guid subscriptionId)
    {
        _context = context;
        _updaterFactory = updaterFactory;
        _logger = logger;
        _subscriptionId = subscriptionId;
        _distributedLock = distributedLock;
    }

    public async Task<SubscriptionUpdateResult> UpdateSubscriptionAsync(
        Subscription subscription,
        Build build,
        bool force = false)
    {
        var pullRequestUpdaterId = PullRequestUpdaterId.CreateUpdaterId(subscription);

        var pullRequestUpdater = _updaterFactory.CreatePullRequestUpdater(pullRequestUpdaterId);

        _logger.LogInformation("Creating pull request updater for subscription {subscriptionId} for branch " +
            "{branch} of {repository}. Is batchable: {batchable}",
            _subscriptionId,
            subscription.TargetBranch,
            subscription.TargetRepository,
            subscription.PolicyObject.Batchable);

        var mutexKey = pullRequestUpdaterId.Id;

        return await _distributedLock.ExecuteWithLockAsync(mutexKey,
            async () =>
            {
                _logger.LogInformation("Running asset update for {subscriptionId}", _subscriptionId);

                var res = await pullRequestUpdater.UpdateAssetsAsync(
                    _subscriptionId,
                    subscription.SourceEnabled
                        ? SubscriptionType.DependenciesAndSources
                        : SubscriptionType.Dependencies,
                    build.Id,
                    applyNewestOnly: false,
                    forceUpdate: force);

                _logger.LogInformation("Asset update complete for {subscriptionId}", _subscriptionId);

                return res;
            });
    }
}
