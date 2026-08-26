// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.DataProviders;
using Maestro.Services.Common.Cache;
using Maestro.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

internal class CodeflowApprovalCheckProcessor : WorkItemProcessor<CodeflowApprovalCheck>
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly IDistributedLock _distributedLock;
    private readonly ILogger<CodeflowApprovalCheckProcessor> _logger;

    public CodeflowApprovalCheckProcessor(
        BuildAssetRegistryContext context,
        IPullRequestUpdaterFactory updaterFactory,
        IDistributedLock distributedLock,
        ILogger<CodeflowApprovalCheckProcessor> logger)
    {
        _context = context;
        _updaterFactory = updaterFactory;
        _distributedLock = distributedLock;
        _logger = logger;
    }

    public override async Task<bool> ProcessWorkItemAsync(CodeflowApprovalCheck workItem, CancellationToken cancellationToken)
    {
        var subscription = await _context.Subscriptions
            .Include(s => s.Channel)
            .FirstOrDefaultAsync(s => s.Id == workItem.SubscriptionId, cancellationToken);
        
        if (subscription == null)
        {
            _logger.LogInformation("Subscription {id} doesn't exist, skipping codeflow approval check", workItem.SubscriptionId);
            return true;
        }

        if (!subscription.AutoApprove)
        {
            throw new InvalidOperationException($"Subscription {subscription.Id} is not auto-approve enabled, cannot run codeflow approval check");
        }

        var pullRequestUpdaterId = PullRequestUpdaterId.CreateUpdaterId(subscription);
        var pullRequestUpdater = _updaterFactory.CreatePullRequestUpdater(pullRequestUpdaterId);

        if (pullRequestUpdater is not CodeFlowPullRequestUpdater codeFlowUpdater)
        {
            throw new InvalidOperationException($"Subscription {subscription.Id} is not a source enabled subscription, cannot run codeflow approval check");
        }

        var mutexKey = pullRequestUpdaterId.Id;

        await _distributedLock.ExecuteWithLockAsync(mutexKey,
            async () => await codeFlowUpdater.RunCodeflowApprovalCheckAsync(
                SqlBarClient.ToClientModelSubscription(subscription),
                workItem,
                cancellationToken),
            cancellationToken: cancellationToken);

        return true;
    }
}
