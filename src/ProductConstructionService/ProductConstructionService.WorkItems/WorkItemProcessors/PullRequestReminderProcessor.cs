// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow;
using ProductConstructionService.WorkItems.WorkItemDefinitions;

namespace ProductConstructionService.WorkItems.WorkItemProcessors;

internal class PullRequestReminderProcessor(
        IActorFactory actorFactory,
        ILogger<PullRequestReminderProcessor> logger)
    : IWorkItemProcessor
{
    private readonly IActorFactory _actorFactory = actorFactory;
    private readonly ILogger<PullRequestReminderProcessor> _logger = logger;

    public async Task<bool> ProcessWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken)
    {
        var reminder = (PullRequestReminderWorkItem)workItem;

        var actor = reminder.SubscriptionId.HasValue
            ? _actorFactory.CreatePullRequestActor(reminder.SubscriptionId.Value)
            : _actorFactory.CreatePullRequestActor(reminder.Repository!, reminder.Branch!);

        switch (reminder.Name)
        {
            case PullRequestActor.PullRequestCheckKey:
                await actor.SynchronizeInProgressPullRequestAsync();
                return true;
            case PullRequestActor.PullRequestUpdateKey:
                return await actor.ProcessPendingUpdatesAsync();
            default:
                throw new Exception($"Reminder {reminder.Name} not found!");
        }
    }
}
