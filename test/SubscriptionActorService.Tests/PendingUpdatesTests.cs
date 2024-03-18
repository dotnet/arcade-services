// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data.Models;
using NUnit.Framework;
using SubscriptionActorService.StateModel;

using Asset = Maestro.Contracts.Asset;

namespace SubscriptionActorService.Tests;

[TestFixture, NonParallelizable]
internal class PendingUpdatesTests : PullRequestActorTests
{
    private async Task WhenProcessPendingUpdatesAsyncIsCalled()
    {
        await Execute(
            async context =>
            {
                PullRequestActor actor = CreateActor(context);
                await actor.Implementation!.ProcessPendingUpdatesAsync();
            });
    }

    private void GivenAPendingUpdateReminder()
    {
        var reminder = new MockReminderManager.Reminder(
            PullRequestActorImplementation.PullRequestUpdateKey,
            [],
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
        Reminders.Data[PullRequestActorImplementation.PullRequestUpdateKey] = reminder;
        ExpectedReminders[PullRequestActorImplementation.PullRequestUpdateKey] = reminder;
    }

    private void AndNoPendingUpdates()
    {
        var updates = new List<UpdateAssetsParameters>();
        StateManager.Data[PullRequestActorImplementation.PullRequestUpdateKey] = updates;
        ExpectedActorState[PullRequestActorImplementation.PullRequestUpdateKey] = updates;
    }

    private void AndPendingUpdates(Build forBuild)
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
                            IsCoherencyUpdate = false
                        }
                };
                StateManager.Data[PullRequestActorImplementation.PullRequestUpdateKey] = updates;
                ExpectedActorState[PullRequestActorImplementation.PullRequestUpdateKey] = updates;
            });
    }

    private void ThenUpdateReminderIsRemoved()
    {
        ExpectedReminders.Remove(PullRequestActorImplementation.PullRequestUpdateKey);
    }

    private void AndPendingUpdateIsRemoved()
    {
        ExpectedActorState.Remove(PullRequestActorImplementation.PullRequestUpdateKey);
    }

    [Test]
    public async Task NoPendingUpdates()
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        GivenAPendingUpdateReminder();
        AndNoPendingUpdates();
        await WhenProcessPendingUpdatesAsyncIsCalled();
        ThenUpdateReminderIsRemoved();
    }

    [Test]
    public async Task PendingUpdatesNotUpdatablePr()
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b = GivenANewBuild(true);

        GivenAPendingUpdateReminder();
        AndPendingUpdates(b);
        using (WithExistingPullRequest(SynchronizePullRequestResult.InProgressCannotUpdate))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled();
            // Nothing happens
        }
    }

    [Test]
    public async Task PendingUpdatesUpdatablePr()
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b = GivenANewBuild(true);

        GivenAPendingUpdateReminder();
        AndPendingUpdates(b);
        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();
        using (WithExistingPullRequest(SynchronizePullRequestResult.InProgressCanUpdate))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled();
            ThenUpdateReminderIsRemoved();
            AndPendingUpdateIsRemoved();
            ThenGetRequiredUpdatesShouldHaveBeenCalled(b);
            AndCommitUpdatesShouldHaveBeenCalled(b);
            AndUpdatePullRequestShouldHaveBeenCalled();
            AndShouldHavePullRequestCheckReminder();
            AndDependencyFlowEventsShouldBeAdded();
        }
    }
}
