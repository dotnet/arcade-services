// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.DataProviders;
using Maestro.WorkItems;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

internal class CodeflowUpdateCheckProcessor : WorkItemProcessor<CodeflowUpdateCheck>
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IPullRequestUpdaterFactory _updaterFactory;

    public CodeflowUpdateCheckProcessor(
        BuildAssetRegistryContext context,
        IPullRequestUpdaterFactory updaterFactory)
    {
        _context = context;
        _updaterFactory = updaterFactory;
    }

    public override async Task<bool> ProcessWorkItemAsync(CodeflowUpdateCheck workItem, CancellationToken cancellationToken)
    {
        var subscription = _context.Subscriptions
            .Include(s => s.Channel)
            .FirstOrDefault(s => s.Id == workItem.SubscriptionId);

        if (subscription == null)
        {
            throw new InvalidOperationException($"Subscription with ID {workItem.SubscriptionId} not found.");
        }

        var pullRequestUpdaterId = PullRequestUpdaterId.CreateUpdaterId(subscription);
        var pullRequestUpdater = _updaterFactory.CreatePullRequestUpdater(pullRequestUpdaterId);

        if (pullRequestUpdater is not CodeFlowPullRequestUpdater codeFlowUpdater)
        {
            throw new InvalidOperationException($"Subscription {subscription.Id} is not a source enabled subscription, cannot run codeflow update check");
        }

        await codeFlowUpdater.RunCodeflowUpdateCodeCheck(
            SqlBarClient.ToClientModelSubscription(subscription),
            workItem,
            cancellationToken);

        return true;
    }
}
