// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class PullRequestCheckProcessor : WorkItemProcessor<InProgressPullRequest>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory;

    public PullRequestCheckProcessor(IPullRequestUpdaterFactory updaterFactory)
    {
        _updaterFactory = updaterFactory;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        InProgressPullRequest workItem,
        CancellationToken cancellationToken)
    {
        var updater = _updaterFactory.CreatePullRequestUpdater(PullRequestUpdaterId.Parse(workItem.ActorId));
        await updater.SynchronizeInProgressPullRequestAsync(workItem);
        return true;
    }
}
