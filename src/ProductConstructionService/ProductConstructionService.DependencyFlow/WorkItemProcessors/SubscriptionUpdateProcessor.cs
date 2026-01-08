// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using ProductConstructionService.DependencyFlow.WorkItems;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class SubscriptionUpdateProcessor(
    IPullRequestUpdaterFactory updaterFactory,
    ISqlBarClient sqlClient,
    ILogger logger)
: DependencyFlowUpdateProcessor<SubscriptionUpdateWorkItem>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory = updaterFactory;
    private readonly ISqlBarClient _sqlClient = sqlClient;
    private readonly ILogger _logger = logger;

    public override async Task<bool> ProcessWorkItemAsync(
        SubscriptionUpdateWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var build = await _sqlClient.GetBuildAsync(workItem.BuildId);
        if (build == null)
        {
            _logger.LogError("Build with buildId {BuildId} not found in the DB.", workItem.BuildId);
            return false;
        }
        var updater = _updaterFactory.CreatePullRequestUpdater(PullRequestUpdaterId.Parse(workItem.UpdaterId));
        await updater.ProcessPendingUpdatesAsync(workItem, applyNewestOnly: true, forceUpdate: false, build);
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
