// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture, NonParallelizable]
internal class PendingCodeFlowUpdatesTests : PendingUpdatePullRequestUpdaterTests
{
    [Test]
    public async Task PendingCodeFlowUpdatesNotUpdatablePr()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build build = GivenANewBuild(true);

        AndPendingUpdates(build, isCodeFlow: true);

        using (WithExistingCodeFlowPullRequest(build, canUpdate: false))
        {
            var res = await WhenProcessPendingUpdatesAsyncIsCalled(build, isCodeFlow: true);
            Assert.That(res.OutcomeType, Is.EqualTo(SubscriptionOutcomeType.Rescheduled));

            AndShouldHaveInProgressPullRequestState(build, build.Id);
        }
    }

    [Test]
    public async Task PendingUpdatesUpdatablePrButNoNewBuild()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build build = GivenANewBuild(true);

        using (WithExistingCodeFlowPullRequest(build, canUpdate: true))
        {
            var res = await WhenProcessPendingUpdatesAsyncIsCalled(build, isCodeFlow: true);
            Assert.That(res.OutcomeType, Is.EqualTo(SubscriptionOutcomeType.NoUpdate));

            AndShouldHaveInProgressPullRequestState(build);
            AndShouldHavePullRequestCheckReminder();
        }
    }

    [Test]
    public async Task PendingUpdatesUpdatablePr()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha123456";

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, willFlowNewBuild: true))
        {
            ExpectPrMetadataToBeUpdated();

            var res = await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true);
            Assert.That(res.OutcomeType, Is.EqualTo(SubscriptionOutcomeType.Success));

            ThenCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(newBuild);
        }
    }

    [Test]
    public async Task PendingUpdatesUpdatableBlockedPr()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha12345";

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, blockedFromFutureUpdates: true))
        {
            var res = await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true);
            Assert.That(res.OutcomeType, Is.EqualTo(SubscriptionOutcomeType.NotUpdatable));

            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
        }
    }

    [Test]
    public async Task PendingUpdatesUpdatablePrNoCodeFlowUpdates()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(new SubscriptionPolicy());
        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha123456";

        WithForwardFlowerReturningNoUpdates();

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true))
        {
            var res = await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true);
            Assert.That(res.OutcomeType, Is.EqualTo(SubscriptionOutcomeType.NoUpdate));

            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(oldBuild);
        }
    }

    [Test]
    public async Task ForcedPendingUpdatesUpdatableBlockedPr()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha12345";

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, blockedFromFutureUpdates: true, willFlowNewBuild: true))
        {
            ExpectPrMetadataToBeUpdated();

            var res = await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true, forceUpdate: true);
            Assert.That(res.OutcomeType, Is.EqualTo(SubscriptionOutcomeType.Success));

            ThenCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(newBuild);
        }
    }
}
