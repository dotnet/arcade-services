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
            await WhenProcessPendingUpdatesAsyncIsCalled(b);

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
        b1.Commit = "sha1";
        Build b2 = GivenANewBuild(true);
        b2.Commit = "sha2";
        Build b3 = GivenANewBuild(true);
        b3.Commit = "sha3";

        using (WithExistingCodeFlowPullRequest(b1, canUpdate: false))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(b2);
            await WhenProcessPendingUpdatesAsyncIsCalled(b3);

            ThenShouldHaveInProgressPullRequestState(b1, b3.Commit);
            ThenShouldHavePendingUpdateState(b3, isCodeFlow: false);
            AndShouldNotHavePullRequestCheckReminder();
        }
    }
}
