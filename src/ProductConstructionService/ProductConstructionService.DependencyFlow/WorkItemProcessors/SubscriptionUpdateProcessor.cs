// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using ProductConstructionService.DependencyFlow.WorkItems;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;

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
        Func<Task<SubscriptionUpdateOutcome>> subscriptionUpdateHandler = () =>
            ProcessSubscriptionUpdateAsync(workItem);

        return await _outcomeRecorder.RunUpdateWithOutcomePersistenceAsync(
            workItem,
            subscriptionUpdateHandler);
    }

    private async Task<SubscriptionUpdateOutcome> ProcessSubscriptionUpdateAsync(
        SubscriptionUpdateWorkItem workItem)
    {
        var build = await _sqlClient.GetBuildAsync(workItem.BuildId);
        if (build == null)
        {
            _logger.LogError("Build with buildId {BuildId} not found in the DB.", workItem.BuildId);
            return SubscriptionUpdateOutcome.Failure;
        }

        var subscription = await _sqlClient.GetSubscriptionAsync(workItem.SubscriptionId);
        if (subscription == null)
        {
            _logger.LogError("Subscription with subscriptionId {SubscriptionId} not found in the DB.", workItem.SubscriptionId);
            return SubscriptionUpdateOutcome.Failure;
        }

        var updater = _updaterFactory.CreatePullRequestUpdater(
            PullRequestUpdaterId.Parse(
                workItem.UpdaterId,
                workItem.SubscriptionType == SubscriptionType.DependenciesAndSources));

        await updater.ProcessPendingUpdatesAsync(
            workItem,
            applyNewestOnly: true,
            forceUpdate: false,
            build);

        return SubscriptionUpdateOutcome.Success;
    }

    protected override Dictionary<string, object> GetLoggingContextData(SubscriptionUpdateWorkItem workItem)
    {
        var data = base.GetLoggingContextData(workItem);
        data["SubscriptionId"] = workItem.SubscriptionId;
        data["BuildId"] = workItem.BuildId;
        return data;
    }
}
