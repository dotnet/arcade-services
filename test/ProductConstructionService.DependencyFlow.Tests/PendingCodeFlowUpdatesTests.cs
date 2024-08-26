// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro.Data.Models;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture, NonParallelizable]
internal class PendingCodeFlowUpdatesTests : PendingUpdatePullRequestActorTests
{
    [Test]
    public async Task PendingCodeFlowUpdatesNotUpdatablePr()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build build = GivenANewBuild(true);

        GivenAPendingUpdateReminder(build, isCodeFlow: true);
        WithExistingCodeFlowStatus(build);
        WithExistingPrBranch();
        AndPendingUpdates(build, isCodeFlow: true);

        using (WithExistingCodeFlowPullRequest(PullRequestStatus.InProgressCannotUpdate))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(build, isCodeFlow: true);

            ThenPcsShouldNotHaveBeenCalled(build, InProgressPrUrl);
            AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
            AndShouldHaveFollowingState(
                pullRequestUpdateReminder: true,
                pullRequestUpdateState: true,
                pullRequestState: true,
                codeFlowState: true);
        }
    }

    [Test]
    public async Task PendingUpdatesUpdatablePrButNoNewBuild()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build build = GivenANewBuild(true);

        GivenAPendingUpdateReminder(build, isCodeFlow: true);
        WithExistingCodeFlowStatus(build);
        WithExistingPrBranch();
        AndPendingUpdates(build, isCodeFlow: true);

        using (WithExistingCodeFlowPullRequest(PullRequestStatus.InProgressCanUpdate))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(build, isCodeFlow: true);

            ThenPcsShouldNotHaveBeenCalled(build, InProgressPrUrl);
            AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
            AndShouldHaveFollowingState(
                pullRequestUpdateReminder: true,
                pullRequestUpdateState: true,
                pullRequestState: true,
                codeFlowState: true);
        }
    }

    [Test]
    public async Task PendingUpdatesUpdatablePr()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha456";

        GivenAPendingUpdateReminder(oldBuild, isCodeFlow: true);
        WithExistingCodeFlowStatus(oldBuild);
        WithExistingPrBranch();
        AndPendingUpdates(newBuild, isCodeFlow: true);
        // TODO: Can we remove this?
        // ExpectPcsToGetCalled(newBuild);

        using (WithExistingCodeFlowPullRequest(PullRequestStatus.InProgressCanUpdate))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(oldBuild, isCodeFlow: true);

            ThenPcsShouldHaveBeenCalled(newBuild, InProgressPrUrl, out var prBranch);
            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
            prBranch.Should().Be(InProgressPrHeadBranch);
            AndShouldHaveCodeFlowState(newBuild, InProgressPrHeadBranch);
            AndShouldHaveFollowingState(
                pullRequestCheckReminder: true,
                pullRequestState: true,
                codeFlowState: true);
        }
    }
}
