// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

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
        Build b = GivenANewBuild(false);
        GivenAPendingUpdateReminder(b);
        AndNoPendingUpdates();
        await WhenProcessPendingUpdatesAsyncIsCalled(b);
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

        GivenAPendingUpdateReminder(b);
        AndPendingUpdates(b);
        using (WithExistingPullRequest(PullRequestStatus.InProgressCannotUpdate))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(b);
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

        GivenAPendingUpdateReminder(b);
        AndPendingUpdates(b);
        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();
        using (WithExistingPullRequest(PullRequestStatus.InProgressCanUpdate))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(b);
            ThenUpdateReminderIsRemoved();
            AndPendingUpdateIsRemoved();
            ThenGetRequiredUpdatesShouldHaveBeenCalled(b, true);
            AndCommitUpdatesShouldHaveBeenCalled(b);
            AndUpdatePullRequestShouldHaveBeenCalled();
            AndShouldHavePullRequestCheckReminder(b);
        }
    }
}
