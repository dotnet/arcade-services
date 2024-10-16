// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class SubscriptionUpdateProcessor : DependencyFlowUpdateProcessor<SubscriptionUpdateWorkItem>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory;

    public SubscriptionUpdateProcessor(
            IPullRequestUpdaterFactory updaterFactory,
            IRedisMutex redisMutex,
            TelemetryClient telemetryClient)
        : base(redisMutex, telemetryClient)
    {
        _updaterFactory = updaterFactory;
    }

    protected override async Task<bool> ProcessUpdateAsync(
        SubscriptionUpdateWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var updater = _updaterFactory.CreatePullRequestUpdater(PullRequestUpdaterId.Parse(workItem.UpdaterId));
        await updater.ProcessPendingUpdatesAsync(workItem);
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
