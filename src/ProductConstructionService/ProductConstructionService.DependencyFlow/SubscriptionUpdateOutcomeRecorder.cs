// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
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
    private readonly BuildAssetRegistryContext _context = context;
    private readonly ILogger<SubscriptionUpdateOutcomeRecorder> _logger = logger;

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
                // Processing a SubscriptionUpdateWorkItem means it's already a rescheduled attempt.
                // There's no need to record the same outcome again.
                return true;
            }

            await RecordSubscriptionUpdateAsync(result.OutcomeMessage, result.OutcomeType, subscriptionId, buildId);
            return true;
        }
        catch (SubscriptionUpdateInputException e)
        {
            _logger.LogError("Encountered user error while processing subscription update for SubscriptionId: {SubscriptionId}, BuildId: {BuildId}. Error: {Error}",
                subscriptionId, buildId, e);
            await RecordSubscriptionUpdateAsync(
                e.Message,
                SubscriptionOutcomeType.UserError,
                subscriptionId,
                buildId);
            return false;
        }
        catch (Exception e)
        {
            if (workItem.IsFinalAttempt() || e is NonRetriableException)
            {
                await RecordSubscriptionUpdateAsync(
                    e.Message,
                    SubscriptionOutcomeType.Failure,
                    subscriptionId,
                    buildId);
            }
            throw;
        }
    }

    private async Task RecordSubscriptionUpdateAsync(
        string message,
        SubscriptionOutcomeType type,
        Guid subscriptionId,
        int? buildId)
    {
        // Fall back to a generated id if there's no current Activity (e.g. tests).
        var operationId = Activity.Current?.RootId ?? Guid.NewGuid().ToString("N");

        await _context.SubscriptionOutcomes.AddAsync(new SubscriptionOutcome
        {
            Message = message,
            OperationId = operationId,
            SubscriptionId = subscriptionId,
            BuildId = buildId ?? -1,
            Type = type,
            Date = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }
}
