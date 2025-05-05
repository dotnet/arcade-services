// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class SubscriptionUpdateProcessor(IPullRequestUpdaterFactory updaterFactory, ISqlBarClient sqlClient)
    : DependencyFlowUpdateProcessor<SubscriptionUpdateWorkItem>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory = updaterFactory;
    private readonly ISqlBarClient _sqlClient = sqlClient;

    public override async Task<bool> ProcessWorkItemAsync(
        SubscriptionUpdateWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var build = await _sqlClient.GetBuildAsync(workItem.BuildId)
            ?? throw new InvalidOperationException($"Build with buildId {workItem.BuildId} not found in the DB.");
        var updater = _updaterFactory.CreatePullRequestUpdater(PullRequestUpdaterId.Parse(workItem.UpdaterId));
        await updater.ProcessPendingUpdatesAsync(workItem, forceApply: false, build);
        return true;
    }

    protected override Dictionary<string, object> GetLoggingContextData(SubscriptionUpdateWorkItem workItem)
    {
        var data = base.GetLoggingContextData(workItem);
        data["SubscriptionId"] = workItem.SubscriptionId;
        data["BuildId"] = workItem.BuildId;
        return data;
    }
}
