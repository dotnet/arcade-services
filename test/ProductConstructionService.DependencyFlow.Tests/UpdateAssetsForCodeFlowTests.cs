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
internal class UpdateAssetsForCodeFlowTests : UpdateAssetsPullRequestUpdaterTests
{
    [Test]
    public async Task UpdateWithNoExistingState()
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
        CreatePullRequestShouldReturnAValidValue();

        await WhenUpdateAssetsAsyncIsCalled(build);

        // TODO (https://github.com/dotnet/arcade-services/issues/3866): We need to populate InProgressPullRequest fully
        // with assets and other info just like we do in UpdatePullRequestAsync.
        // Right now, we are not flowing packages in codeflow subscriptions yet, so this functionality is no there
        // For now, we manually update the info the unit tests expect.
        var expectedState = new WorkItems.InProgressPullRequest()
        {
            ActorId = GetPullRequestUpdaterId(Subscription).Id,
            Url = VmrPullRequestUrl,
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
        AndCodeFlowPullRequestShouldHaveBeenCreated();
        AndCodeShouldHaveBeenFlownForward(build);
        AndShouldHaveCodeFlowState(build, InProgressVmrPrHeadBranch);
        AndShouldHavePullRequestCheckReminder(build, expectedState);
        AndShouldHaveInProgressPullRequestState(build, expectedState: expectedState);
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
        using (WithExistingCodeFlowPullRequest(build, canUpdate: false))
        {
            await WhenUpdateAssetsAsyncIsCalled(build);

            ThenShouldHavePendingUpdateState(build);
            AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
            AndShouldHaveInProgressPullRequestState(build, coherencyCheckSuccessful: true);
        }
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
        using (WithExistingCodeFlowPullRequest(build, canUpdate: true))
        {
            await WhenUpdateAssetsAsyncIsCalled(build);

            AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
            AndShouldHavePullRequestCheckReminder(build);
            AndShouldHaveInProgressPullRequestState(build);
        }
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

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true))
        {
            await WhenUpdateAssetsAsyncIsCalled(newBuild);

            ThenShouldHaveInProgressPullRequestState(newBuild);
            AndCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHaveCodeFlowState(newBuild, InProgressPrHeadBranch);
            AndShouldHavePullRequestCheckReminder(newBuild);
        }
    }

    [Test]
    public async Task UpdateWithManuallyMergedPrAndNewBuild()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            });
        Build build = GivenANewBuild(true);
        Build build2 = GivenANewBuild(true);

        WithExistingCodeFlowStatus(build);
        using (WithExistingCodeFlowPullRequest(build, PrStatus.Merged, null))
        {
            // Original PR is merged, we should try to delete the branch
            DarcRemotes[VmrUri]
                .Setup(x => x.DeletePullRequestBranchAsync(VmrPullRequestUrl))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // URI of the new PR that should get created
            VmrPullRequestUrl = $"{VmrUri}/pulls/2";
            CreatePullRequestShouldReturnAValidValue();

            await WhenUpdateAssetsAsyncIsCalled(build2);

            AndShouldHaveCodeFlowState(build2, InProgressVmrPrHeadBranch);

            // TODO (https://github.com/dotnet/arcade-services/issues/3866): We need to populate InProgressPullRequest fully
            // with assets and other info just like we do in UpdatePullRequestAsync.
            // Right now, we are not flowing packages in codeflow subscriptions yet, so this functionality is no there
            // For now, we manually update the info the unit tests expect.
            var expectedState = new WorkItems.InProgressPullRequest()
            {
                ActorId = GetPullRequestUpdaterId(Subscription).Id,
                Url = VmrPullRequestUrl,
                ContainedSubscriptions =
                [
                    new()
                    {
                        SubscriptionId = Subscription.Id,
                        BuildId = build2.Id,
                    }
                ]
            };

            AndShouldHavePullRequestCheckReminder(build2, expectedState);
            AndShouldHaveInProgressPullRequestState(build2, expectedState: expectedState);
        }
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
