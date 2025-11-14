// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture, NonParallelizable]
internal class PendingUpdatesTests : PendingUpdatePullRequestUpdaterTests
{
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
        using (WithExistingPullRequest(b, canUpdate: false))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(b);
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
        using (WithExistingPullRequest(b, canUpdate: true))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(b, shouldGetUpdates: true);

            ThenGetRequiredUpdatesShouldHaveBeenCalled(b, true);
            ThenUpdateReminderIsRemoved();
            AndPendingUpdateIsRemoved();
            AndCommitUpdatesShouldHaveBeenCalled(b);
            AndUpdatePullRequestShouldHaveBeenCalled();
            AndShouldHavePullRequestCheckReminder();
        }
    }

    [Test]
    public async Task PendingUpdatesNotUpdatableGroupingTest()
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b1 = GivenANewBuild(true);
        Build b2 = GivenANewBuild(true);
        b2.Id = 2;

        using (WithExistingPullRequest(b1, canUpdate: false))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(b2);

            ThenShouldHaveInProgressPullRequestState(b1, b2.Id);
            ThenShouldHavePendingUpdateState(b2, isCodeFlow: false);
            AndShouldNotHavePullRequestCheckReminder();
        }
    }

    [Test]
    public async Task PendingUpdatesShouldNotBeProcessedUnlessNewerBuildQueued()
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b1 = GivenANewBuild(true);
        Build b2 = GivenANewBuild(true);
        b2.Id = 2;
        Build b3 = GivenANewBuild(true);
        b3.Id = 3;

        using (WithExistingPullRequest(b1, canUpdate: true, nextBuildToProcess: b3.Id, setupRemoteMock: false))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(b2, applyNewestOnly: true);

            ThenShouldHaveInProgressPullRequestState(b1, b3.Id);
            AndShouldHaveNoPendingUpdateState();
            AndShouldNotHavePullRequestCheckReminder();
        }
    }

    [Test]
    public async Task PendingUpdatesShouldBeProcessedWhenNewestBuildPending()
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b1 = GivenANewBuild(true);
        Build b2 = GivenANewBuild(true);
        b2.Id = 2;

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();
        using (WithExistingPullRequest(b1, canUpdate: true, nextBuildToProcess: b2.Id, setupRemoteMock: true))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(b2, applyNewestOnly: true, shouldGetUpdates: true);

            ThenShouldHaveInProgressPullRequestState(b2);
            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
        }
    }

    [Test]
    public async Task PendingUpdatesShouldBeForceProcessedWhenNotUpdatablePr()
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
        using (WithExistingPullRequest(b, canUpdate: false))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(b, forceUpdate: true, shouldGetUpdates: true);

            ThenGetRequiredUpdatesShouldHaveBeenCalled(b, true);
            ThenUpdateReminderIsRemoved();
            AndPendingUpdateIsRemoved();
            AndCommitUpdatesShouldHaveBeenCalled(b);
            AndUpdatePullRequestShouldHaveBeenCalled();
            AndShouldHavePullRequestCheckReminder();
        }
    }
}
