// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using Maestro.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class SubscriptionUpdateProcessor(
    IPullRequestUpdaterFactory updaterFactory,
    ISqlBarClient sqlClient,
    ISubscriptionUpdateOutcomeRecorder outcomeRecorder,
    ILogger logger)
: DependencyFlowUpdateProcessor<SubscriptionUpdateWorkItem>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory = updaterFactory;
    private readonly ISqlBarClient _sqlClient = sqlClient;
    private readonly ISubscriptionUpdateOutcomeRecorder _outcomeRecorder = outcomeRecorder;
    private readonly ILogger _logger = logger;

    public override async Task<bool> ProcessWorkItemAsync(
        SubscriptionUpdateWorkItem workItem,
        CancellationToken cancellationToken)
    {
        return await _outcomeRecorder.RunUpdateWithOutcomePersistenceAsync(
            workItem,
            () => ProcessSubscriptionUpdateAsync(workItem));
    }

    private async Task<SubscriptionUpdateResult> ProcessSubscriptionUpdateAsync(
        SubscriptionUpdateWorkItem workItem)
    {
        var build = await _sqlClient.GetBuildAsync(workItem.BuildId)
            ?? throw new NonRetriableException($"Build with buildId {workItem.BuildId} not found in the DB.");

        var updater = _updaterFactory.CreatePullRequestUpdater(
            PullRequestUpdaterId.Parse(
                workItem.UpdaterId,
                workItem.SubscriptionType == SubscriptionType.DependenciesAndSources));

        return await updater.ProcessPendingUpdatesAsync(
            workItem,
            applyNewestOnly: true,
            forceUpdate: false,
            build);
    }

    protected override Dictionary<string, object> GetLoggingContextData(SubscriptionUpdateWorkItem workItem)
    {
        var data = base.GetLoggingContextData(workItem);
        data["SubscriptionId"] = workItem.SubscriptionId;
        data["BuildId"] = workItem.BuildId;
        return data;
    }
}
