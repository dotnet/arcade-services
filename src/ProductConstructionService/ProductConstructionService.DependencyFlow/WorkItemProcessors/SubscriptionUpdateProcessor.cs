// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class SubscriptionUpdateProcessor : WorkItemProcessor<SubscriptionUpdateWorkItem>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly IRedisMutex _redisMutex;

    public SubscriptionUpdateProcessor(IPullRequestUpdaterFactory updaterFactory, IRedisMutex redisMutex)
    {
        _updaterFactory = updaterFactory;
        _redisMutex = redisMutex;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        SubscriptionUpdateWorkItem workItem,
        CancellationToken cancellationToken)
    {
        return await _redisMutex.ExecuteWhenReady(
            workItem.ActorId,
            async () =>
            {
                var updater = _updaterFactory.CreatePullRequestUpdater(PullRequestUpdaterId.Parse(workItem.ActorId));
                await updater.ProcessPendingUpdatesAsync(workItem);
                return true;
            }); 
    }
}
