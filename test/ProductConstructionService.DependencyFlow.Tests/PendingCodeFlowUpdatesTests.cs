// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
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
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build build = GivenANewBuild(true);

        WithExistingCodeFlowStatus(build);
        WithExistingPrBranch();
        AndPendingUpdates(build, isCodeFlow: true);

        WithExistingCodeFlowPullRequest(build, canUpdate: false);
        await WhenProcessPendingUpdatesAsyncIsCalled(build, isCodeFlow: true);

        ThenPcsShouldNotHaveBeenCalled(build, InProgressPrUrl);
        AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
        AndShouldHaveInProgressPullRequestState(build);
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

        WithExistingCodeFlowStatus(build);
        WithExistingPrBranch();
        WithExistingCodeFlowPullRequest(build, canUpdate: true);

        await WhenProcessPendingUpdatesAsyncIsCalled(build, isCodeFlow: true);

        ThenPcsShouldNotHaveBeenCalled(build, InProgressPrUrl);
        AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
        AndShouldHaveInProgressPullRequestState(build);
        AndShouldHavePullRequestCheckReminder(build);
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

        WithExistingCodeFlowStatus(oldBuild);
        WithExistingPrBranch();

        WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true);
        await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true);

        ThenPcsShouldHaveBeenCalled(newBuild, InProgressPrUrl, out var prBranch);
        AndShouldHaveNoPendingUpdateState();
        AndShouldHavePullRequestCheckReminder(newBuild);
        prBranch.Should().Be(InProgressPrHeadBranch);
        AndShouldHaveCodeFlowState(newBuild, InProgressPrHeadBranch);
        AndShouldHaveInProgressPullRequestState(newBuild);
    }
}
