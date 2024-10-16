// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class SubscriptionUpdateProcessor : DependencyFlowUpdateProcessor<SubscriptionUpdateWorkItem>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory;

    public SubscriptionUpdateProcessor(
            IPullRequestUpdaterFactory updaterFactory,
            IRedisMutex redisMutex,
            ILogger<SubscriptionUpdateProcessor> logger)
        : base(redisMutex, logger)
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

    protected override Dictionary<string, object> GetLoggingScopeData(SubscriptionUpdateWorkItem workItem)
    {
        var data = base.GetLoggingScopeData(workItem);
        data["SubscriptionId"] = workItem.SubscriptionId;
        data["BuildId"] = workItem.BuildId;
        return data;
    }
}
