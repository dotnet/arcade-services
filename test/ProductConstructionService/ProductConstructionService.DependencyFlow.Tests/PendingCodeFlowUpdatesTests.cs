// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Moq;
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

            ShouldHaveSubscriptionUpdateResult(SubscriptionOutcomeType.Rescheduled, res.OutcomeType);
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

            ShouldHaveSubscriptionUpdateResult(SubscriptionOutcomeType.NoUpdate, res.OutcomeType);
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
            Assert.That(res.OutcomeType, Is.EqualTo(SubscriptionOutcomeType.Updated));

            AndCodeShouldHaveBeenFlownForward(newBuild);
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task BlockingCodeflowExceptionBlocksExistingPr(bool recreatingOppositeFlow)
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(new SubscriptionPolicy
        {
            Batchable = false,
            UpdateFrequency = UpdateFrequency.EveryBuild
        });
        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha123456";
        BlockingCodeflowException exception = recreatingOppositeFlow
            ? new OppositeDirectionFlowRecreationException()
            : new BlockingCodeflowException("An opposite direction flow has been merged.");
        WithForwardFlowThrowingBlockingException(exception);

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true))
        {
            var remote = DarcRemotes[Subscription.TargetRepository];
            remote.Setup(r => r.GetPullRequestCommentsAsync(VmrPullRequestUrl))
                .ReturnsAsync([]);
            remote.Setup(r => r.CommentPullRequestAsync(
                    VmrPullRequestUrl,
                    It.Is<string>(text => text.Contains(PullRequestCommentBuilder.BlockedCodeflowNotificationIntro)
                        && text.Contains("haven't been flown yet"))))
                .Returns(Task.CompletedTask);

            var result = await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true);

            Assert.That(result.OutcomeType, Is.EqualTo(SubscriptionOutcomeType.NotUpdatable));
            Assert.That(result.OutcomeMessage, Is.EqualTo(exception.Message));
            AndShouldHaveInProgressPullRequestState(oldBuild, blockedFromFutureUpdates: true);
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

            ShouldHaveSubscriptionUpdateResult(SubscriptionOutcomeType.NoUpdate, res.OutcomeType);
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

            ShouldHaveSubscriptionUpdateResult(SubscriptionOutcomeType.Updated, res.OutcomeType);
            AndCodeShouldHaveBeenFlownForward(newBuild, forceUpdate: true);
            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(newBuild);
        }
    }
}
