// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Maestro.WorkItems;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;

namespace ProductConstructionService.DependencyFlow;

public interface ISubscriptionUpdateOutcomeRecorder
{
    Task<bool> RunUpdateWithOutcomePersistenceAsync(
        SubscriptionUpdateWorkItem workItem,
        Func<Task<SubscriptionUpdateResult>> processAsync);

    Task<bool> RunUpdateWithOutcomePersistenceAsync(
        SubscriptionTriggerWorkItem workItem,
        Func<Task<SubscriptionUpdateResult>> processAsync);
}

public class SubscriptionUpdateOutcomeRecorder(
    BuildAssetRegistryContext context,
    ILogger<SubscriptionUpdateOutcomeRecorder> logger) : ISubscriptionUpdateOutcomeRecorder
{
    private readonly ILogger<SubscriptionUpdateOutcomeRecorder> _logger = logger;
    private readonly BuildAssetRegistryContext _context = context;

    public Task<bool> RunUpdateWithOutcomePersistenceAsync(
        SubscriptionUpdateWorkItem workItem,
        Func<Task<SubscriptionUpdateResult>> processAsync) =>
        RunUpdateWithOutcomePersistenceAsync(workItem, workItem.SubscriptionId, workItem.BuildId, processAsync);

    public Task<bool> RunUpdateWithOutcomePersistenceAsync(
        SubscriptionTriggerWorkItem workItem,
        Func<Task<SubscriptionUpdateResult>> processAsync) =>
        RunUpdateWithOutcomePersistenceAsync(workItem, workItem.SubscriptionId, workItem.BuildId, processAsync);

    private async Task<bool> RunUpdateWithOutcomePersistenceAsync(
        WorkItem workItem,
        Guid subscriptionId,
        int? buildId,
        Func<Task<SubscriptionUpdateResult>> processAsync)
    {
        try
        {
            var result = await processAsync();
            if (result.OutcomeType is SubscriptionOutcomeType.Rescheduled
                && workItem is SubscriptionUpdateWorkItem)
            {
                // Workitem will be retried in 5 minutes - we don't want to record any outcome now.
                return true;
            }

            await RecordSubscriptionUpdateAsync(result.OutcomeMessage, result.OutcomeType, subscriptionId, buildId);
            return true;
        }
        catch (Exception e)
        {
            if (workItem.IsFinalAttempt())
            {
                await RecordFailureAsync(e.Message, subscriptionId, buildId);
            }
            throw;
        }
    }

    private async Task RecordFailureAsync(
        string message,
        Guid subscriptionId,
        int? buildId)
    {
        await _context.SubscriptionOutcomes.AddAsync(new SubscriptionOutcome
        {
            Message = message,
            OperationId = Guid.NewGuid().ToString(),
            SubscriptionId = subscriptionId,
            BuildId = buildId ?? -1,
            Type = SubscriptionOutcomeType.Failure,
            Date = DateTime.UtcNow
        });
    }

    private async Task RecordSubscriptionUpdateAsync(
        string message,
        SubscriptionOutcomeType type,
        Guid subscriptionId,
        int? buildId)
    {
        await _context.SubscriptionOutcomes.AddAsync(new SubscriptionOutcome
        {
            Message = message,
            OperationId = Guid.NewGuid().ToString(),
            SubscriptionId = subscriptionId,
            BuildId = buildId ?? -1,
            Type = type,
            Date = DateTime.UtcNow
        });
    }

}
