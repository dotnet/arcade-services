// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class PullRequestCheckProcessor(IPullRequestUpdaterFactory updaterFactory)
    : DependencyFlowUpdateProcessor<PullRequestCheck>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory = updaterFactory;

    public override async Task<bool> ProcessWorkItemAsync(
        PullRequestCheck workItem,
        CancellationToken cancellationToken)
    {
        var updater = _updaterFactory.CreatePullRequestUpdater(PullRequestUpdaterId.Parse(workItem.UpdaterId));
        return await updater.CheckPullRequestAsync(workItem);
    }

    protected override Dictionary<string, object> GetLoggingContextData(PullRequestCheck workItem)
    {
        var data = base.GetLoggingContextData(workItem);
        data["PrUrl"] = workItem.Url;
        return data;
    }
}
