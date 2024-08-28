// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

/// <summary>
/// Tests the code flow PR update logic.
/// The tests are writter in the order in which the different phases of the PR are written.
/// Each test should have the inner state that is left behind by the previous state.
/// </summary>
[TestFixture, NonParallelizable]
internal class UpdateAssetsForCodeFlowTests : UpdateAssetsPullRequestUpdaterTests
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

        // TODO (https://github.com/dotnet/arcade-services/issues/3866): We need to populate InProgressPullRequest fully
        // with assets and other info just like we do in UpdatePullRequestAsync.
        // Right now, we are not flowing packages in codeflow subscriptions yet, so this functionality is no there
        // For now, we manually update the info the unit tests expect.
        var expectedState = new WorkItems.InProgressPullRequest()
        {
            ActorId = GetPullRequestUpdaterId(Subscription).Id,
            Url = InProgressPrUrl,
            ContainedSubscriptions =
            [
                new()
                {
                    SubscriptionId = Subscription.Id,
                    BuildId = build.Id,
                }
            ]
        };

        ThenUpdateReminderIsRemoved();
        ThenPcsShouldNotHaveBeenCalled(build);
        AndCodeFlowPullRequestShouldHaveBeenCreated();
        AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
        AndShouldHavePullRequestCheckReminder(build, expectedState);
        AndShouldHaveInProgressPullRequestState(build, coherencyCheckSuccessful: null, expectedState: expectedState);
        AndPendingUpdateIsRemoved();
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
        WithExistingPullRequest(build, canUpdate: false);

        await WhenUpdateAssetsAsyncIsCalled(build);

        ThenShouldHavePendingUpdateState(build);
        ThenPcsShouldNotHaveBeenCalled(build, InProgressPrUrl);
        AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
        AndShouldHaveInProgressPullRequestState(build, coherencyCheckSuccessful: true);
        AndShouldHavePullRequestCheckReminder(build);
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

        WithExistingCodeFlowStatus(build);
        WithExistingPrBranch();
        WithExistingPullRequest(build, canUpdate: true);

        await WhenUpdateAssetsAsyncIsCalled(build);

        ThenPcsShouldNotHaveBeenCalled(build, InProgressPrUrl);
        AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
        AndShouldHavePullRequestCheckReminder(build);
        AndShouldHaveInProgressPullRequestState(build);
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

        WithExistingCodeFlowStatus(oldBuild);
        WithExistingPrBranch();
        WithExistingPullRequest(oldBuild, canUpdate: true);

        await WhenUpdateAssetsAsyncIsCalled(newBuild);

        ThenPcsShouldHaveBeenCalled(newBuild, InProgressPrUrl, out _);
        AndShouldHaveCodeFlowState(newBuild, InProgressPrHeadBranch);
        AndShouldHavePullRequestCheckReminder(newBuild);
        AndShouldHaveInProgressPullRequestState(newBuild);
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
