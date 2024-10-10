// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class PullRequestCheckProcessor : WorkItemProcessor<InProgressPullRequest>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly IRedisMutex _redisMutex;

    public PullRequestCheckProcessor(IPullRequestUpdaterFactory updaterFactory, IRedisMutex redisMutex)
    {
        _updaterFactory = updaterFactory;
        _redisMutex = redisMutex;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        InProgressPullRequest workItem,
        CancellationToken cancellationToken)
    {
        return await _redisMutex.EnterWhenAvailable(
            workItem.ActorId,
            async () =>
            {
                var updater = _updaterFactory.CreatePullRequestUpdater(PullRequestUpdaterId.Parse(workItem.ActorId));
                return await updater.CheckInProgressPullRequestAsync(workItem);
            });
    }
}
