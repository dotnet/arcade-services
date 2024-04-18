// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data.Models;
using SubscriptionActorService.StateModel;

using Asset = Maestro.Contracts.Asset;

namespace SubscriptionActorService.Tests;

internal abstract class PendingUpdatePullRequestActorTests : PullRequestActorTests
{
    protected async Task WhenProcessPendingUpdatesAsyncIsCalled()
    {
        await Execute(
            async context =>
            {
                PullRequestActor actor = CreateActor(context);
                await actor.Implementation!.ProcessPendingUpdatesAsync();
            });
    }

    protected void GivenAPendingUpdateReminder()
    {
        var reminder = new MockReminderManager.Reminder(
            PullRequestActorImplementation.PullRequestUpdateKey,
            [],
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
        Reminders.Data[PullRequestActorImplementation.PullRequestUpdateKey] = reminder;
        ExpectedReminders[PullRequestActorImplementation.PullRequestUpdateKey] = reminder;
    }

    protected void AndNoPendingUpdates()
    {
        var updates = new List<UpdateAssetsParameters>();
        StateManager.Data[PullRequestActorImplementation.PullRequestUpdateKey] = updates;
        ExpectedActorState[PullRequestActorImplementation.PullRequestUpdateKey] = updates;
    }

    protected void AndPendingUpdates(Build forBuild, bool isCodeFlow = false)
    {
        AfterDbUpdateActions.Add(
            () =>
            {
                var updates = new List<UpdateAssetsParameters>
                {
                        new()
                        {
                            SubscriptionId = Subscription.Id,
                            BuildId = forBuild.Id,
                            SourceRepo = forBuild.GitHubRepository ?? forBuild.AzureDevOpsRepository,
                            SourceSha = forBuild.Commit,
                            Assets = forBuild.Assets
                                .Select(a => new Asset {Name = a.Name, Version = a.Version})
                                .ToList(),
                            IsCoherencyUpdate = false,
                            IsCodeFlow = isCodeFlow,
                        }
                };
                StateManager.Data[PullRequestActorImplementation.PullRequestUpdateKey] = updates;
                ExpectedActorState[PullRequestActorImplementation.PullRequestUpdateKey] = updates;
            });
    }
}
