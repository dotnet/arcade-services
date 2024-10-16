// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class PullRequestCheckProcessor : WorkItemProcessor<PullRequestCheck>
{
    private readonly IPullRequestUpdaterFactory _updaterFactory;
    private readonly IRedisMutex _redisMutex;
    private readonly IReminderManagerFactory _reminderFactory;

    public PullRequestCheckProcessor(
        IPullRequestUpdaterFactory updaterFactory,
        IRedisMutex redisMutex,
        IReminderManagerFactory reminderFactory)
    {
        _updaterFactory = updaterFactory;
        _redisMutex = redisMutex;
        _reminderFactory = reminderFactory;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        PullRequestCheck workItem,
        CancellationToken cancellationToken)
    {
        return await _redisMutex.EnterWhenAvailable(
            workItem.UpdaterId,
            async () =>
            {
                var reminders = _reminderFactory.CreateReminderManager<PullRequestCheck>(workItem.UpdaterId);
                await reminders.ReminderReceivedAsync();

                var updater = _updaterFactory.CreatePullRequestUpdater(PullRequestUpdaterId.Parse(workItem.UpdaterId));
                return await updater.CheckPullRequestAsync(workItem);
            });
    }
}
