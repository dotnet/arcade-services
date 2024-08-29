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
        WithExistingPullRequest(b, canUpdate: false);
        await WhenProcessPendingUpdatesAsyncIsCalled(b);
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
        WithExistingPullRequest(b, canUpdate: true);

        await WhenProcessPendingUpdatesAsyncIsCalled(b);

        ThenGetRequiredUpdatesShouldHaveBeenCalled(b, true);
        ThenUpdateReminderIsRemoved();
        AndPendingUpdateIsRemoved();
        AndCommitUpdatesShouldHaveBeenCalled(b);
        AndUpdatePullRequestShouldHaveBeenCalled();
        AndShouldHavePullRequestCheckReminder(b);
    }
}
