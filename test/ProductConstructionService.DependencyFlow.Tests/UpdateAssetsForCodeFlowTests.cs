// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib.Models;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

/// <summary>
/// Tests the code flow PR update logic.
/// The tests are writer in the order in which the different phases of the PR are written.
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
        var expectedState = new InProgressPullRequest()
        {
            UpdaterId = GetPullRequestUpdaterId(Subscription).Id,
            Url = VmrPullRequestUrl,
            HeadBranch = InProgressPrHeadBranch,
            SourceSha = build.Commit,
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
        AndShouldHavePullRequestCheckReminder();
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

        using (WithExistingCodeFlowPullRequest(build, canUpdate: false))
        {
            await WhenUpdateAssetsAsyncIsCalled(build);

            ThenShouldHavePendingUpdateState(build);
            AndShouldHaveInProgressPullRequestState(
                build,
                nextBuildToProcess: build.Id,
                coherencyCheckSuccessful: true);
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

        using (WithExistingCodeFlowPullRequest(build, canUpdate: true))
        {
            await WhenUpdateAssetsAsyncIsCalled(build);

            AndShouldHavePullRequestCheckReminder();
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
        newBuild.Commit = "sha123456";

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true))
        {
            ExpectPrMetadataToBeUpdated();

            await WhenUpdateAssetsAsyncIsCalled(newBuild);

            ThenShouldHaveInProgressPullRequestState(newBuild);
            AndCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHavePullRequestCheckReminder();
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

            // TODO (https://github.com/dotnet/arcade-services/issues/3866): We need to populate InProgressPullRequest fully
            // with assets and other info just like we do in UpdatePullRequestAsync.
            // Right now, we are not flowing packages in codeflow subscriptions yet, so this functionality is no there
            // For now, we manually update the info the unit tests expect.
            var expectedState = new InProgressPullRequest()
            {
                UpdaterId = GetPullRequestUpdaterId(Subscription).Id,
                Url = VmrPullRequestUrl,
                HeadBranch = InProgressPrHeadBranch,
                SourceSha = build2.Commit,
                ContainedSubscriptions =
                [
                    new()
                    {
                        SubscriptionId = Subscription.Id,
                        BuildId = build2.Id,
                    }
                ]
            };

            AndShouldHavePullRequestCheckReminder();
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
