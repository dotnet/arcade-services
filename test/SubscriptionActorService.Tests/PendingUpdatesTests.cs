// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Maestro.Data.Models;
using NUnit.Framework;
using SubscriptionActorService.StateModel;

namespace SubscriptionActorService.Tests;

[TestFixture, NonParallelizable]
internal class PendingUpdatesTests : PendingUpdatePullRequestActorTests
{
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
