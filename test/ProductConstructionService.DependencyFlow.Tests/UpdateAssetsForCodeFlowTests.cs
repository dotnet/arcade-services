// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

/// <summary>
/// Tests the code flow PR update logic.
/// The tests are writter in the order in which the different phases of the PR are written.
/// Each test should have the inner state that is left behind by the previous state.
/// </summary>
[TestFixture, NonParallelizable]
internal class UpdateAssetsForCodeFlowTests : UpdateAssetsPullRequestActorTests
{
    [Test]
    public async Task UpdateWithNoExistingStateOrPrBranch()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            });
        Build build = GivenANewBuild(true);

        await WhenUpdateAssetsAsyncIsCalled(build);

        ThenShouldHavePendingUpdateState(build);
        AndPcsShouldHaveBeenCalled(build, prUrl: null, out var requestedBranch);
        AndShouldHaveCodeFlowState(build, requestedBranch);
        AndShouldHaveFollowingState(
            pullRequestState: true,
            pullRequestUpdateReminder: true,
            codeFlowState: true);
    }

    [Test]
    public async Task WaitForPrBranch()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            });
        Build build = GivenANewBuild(true);

        GivenPendingUpdates(build);
        WithExistingCodeFlowStatus(build);
        WithoutExistingPrBranch();

        await WhenUpdateAssetsAsyncIsCalled(build);

        ThenShouldHavePendingUpdateState(build);
        AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
        AndShouldHaveFollowingState(
            codeFlowState: true,
            pullRequestState: true,
            pullRequestUpdateReminder: true);
    }

    [Test]
    public async Task UpdateWithPrBranchReady()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            });
        Build build = GivenANewBuild(true);

        GivenPendingUpdates(build);
        WithExistingCodeFlowStatus(build);
        WithExistingPrBranch();
        CreatePullRequestShouldReturnAValidValue();

        await WhenUpdateAssetsAsyncIsCalled(build);

        ThenUpdateReminderIsRemoved();
        ThenPcsShouldNotHaveBeenCalled(build);
        AndCodeFlowPullRequestShouldHaveBeenCreated();
        AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
        AndShouldHavePullRequestCheckReminder(build);
        AndShouldHaveInProgressCodeFlowPullRequestState(build);
        AndPendingUpdateIsRemoved();
        AndShouldHaveFollowingState(
            codeFlowState: true,
            pullRequestState: true,
            pullRequestCheckReminder: true);
    }

    [Test]
    public async Task UpdateWithPrNotUpdatable()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            });
        Build build = GivenANewBuild(true);

        WithExistingCodeFlowStatus(build);
        WithExistingPrBranch();

        WithExistingCodeFlowPullRequest(build, PrStatus.Open, null);
        await WhenUpdateAssetsAsyncIsCalled(build);

        ThenShouldHavePendingUpdateState(build);
        ThenPcsShouldNotHaveBeenCalled(build, InProgressPrUrl);
        AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
        AndShouldHaveFollowingState(
            codeFlowState: true,
            pullRequestState: true,
            pullRequestUpdateReminder: true);
    }

    [Test]
    public async Task UpdateWithPrUpdatableButNoUpdates()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            });
        Build build = GivenANewBuild(true);

        GivenAPullRequestCheckReminder(build);
        WithExistingCodeFlowStatus(build);
        WithExistingPrBranch();

        WithExistingCodeFlowPullRequest(build, PrStatus.Open, null);
        await WhenUpdateAssetsAsyncIsCalled(build);

        ThenPcsShouldNotHaveBeenCalled(build, InProgressPrUrl);
        AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
        AndShouldHavePullRequestCheckReminder(build);
        AndShouldHaveFollowingState(
            codeFlowState: true,
            pullRequestState: true,
            pullRequestCheckReminder: true);
    }

    [Test]
    public async Task UpdateCodeFlowPrWithNewBuild()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            });

        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha456";

        GivenAPullRequestCheckReminder(oldBuild);
        WithExistingCodeFlowStatus(oldBuild);
        WithExistingPrBranch();

        WithExistingCodeFlowPullRequest(oldBuild, PrStatus.Open, null);
        await WhenUpdateAssetsAsyncIsCalled(newBuild);

        ThenPcsShouldHaveBeenCalled(newBuild, InProgressPrUrl, out _);
        AndShouldHaveCodeFlowState(newBuild, InProgressPrHeadBranch);
        AndShouldHavePullRequestCheckReminder(newBuild);
        AndShouldHaveFollowingState(
            codeFlowState: true,
            pullRequestState: true,
            pullRequestCheckReminder: true);
    }

    protected override void ThenShouldHavePendingUpdateState(Build forBuild, bool _ = false)
    {
        base.ThenShouldHavePendingUpdateState(forBuild, true);
    }

    protected void GivenPendingUpdates(Build forBuild)
    {
        AfterDbUpdateActions.Add(
            () =>
            {
                var update = CreateSubscriptionUpdate(forBuild, isCodeFlow: false);
                SetExpectedReminder(Subscription, update);
            });
    }
}
