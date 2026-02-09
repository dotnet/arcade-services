// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.Data.Models;
using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Moq;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.Model;

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
            TargetBranch = TargetBranch,
            HeadBranch = InProgressPrHeadBranch,
            HeadBranchSha = InProgressPrHeadBranchSha,
            SourceSha = build.Commit,
            ContainedSubscriptions =
            [
                new()
                {
                    SubscriptionId = Subscription.Id,
                    BuildId = build.Id,
                    SourceRepo = build.GetRepository(),
                    CommitSha = build.Commit
                }
            ],
            RequiredUpdates = [],
            CodeFlowDirection = CodeFlowDirection.ForwardFlow,
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

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, willFlowNewBuild: true))
        {
            ExpectPrMetadataToBeUpdated();

            await WhenUpdateAssetsAsyncIsCalled(newBuild);

            ThenShouldHaveInProgressPullRequestState(newBuild);
            AndCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHavePullRequestCheckReminder();
        }
    }

    [Test]
    public async Task UpdateCodeFlowWithNoPrWithConflict()
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

        WithForwardFlowConflict(
            DarcRemotes[Subscription.TargetRepository],
            [new UnixPath($"src/{Subscription.TargetDirectory}/conflict.txt")]);

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
            HeadBranchSha = InProgressPrHeadBranchSha,
            SourceSha = build.Commit,
            ContainedSubscriptions =
            [
                new()
                {
                    SubscriptionId = Subscription.Id,
                    BuildId = build.Id,
                    SourceRepo = build.GetRepository(),
                    CommitSha = build.Commit
                }
            ],
            RequiredUpdates = [],
            CodeFlowDirection = CodeFlowDirection.ForwardFlow,
        };

        ThenUpdateReminderIsRemoved();
        AndCodeFlowPullRequestShouldHaveBeenCreated();
        AndCodeShouldHaveBeenFlownForward(build);
        AndShouldHavePullRequestCheckReminder();
        AndShouldHaveInProgressPullRequestState(build, expectedState: expectedState);
        AndPendingUpdateIsRemoved();
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

        using (WithExistingCodeFlowPullRequest(build, PrStatus.Merged, null, willFlowNewBuild: true))
        {
            // Original PR is merged, we should try to delete the branch
            DarcRemotes[VmrUri]
                .Setup(x => x.DeletePullRequestBranchAsync(VmrPullRequestUrl))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // We must mock checking for which builds were applied in the merged PR
            DarcRemotes[VmrUri]
                .Setup(x => x.GetSourceManifestAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new SourceManifest(
                    [
                        new RepositoryRecord(
                            Subscription.TargetDirectory,
                            build.GetRepository(),
                            build.Commit,
                            build.Id),
                        new RepositoryRecord(
                            "another-repo",
                            "https://github.com/another/repo",
                            "abcdef01234234423",
                            build.Id + 100),
                    ], []))
                .Verifiable();

            // URI of the new PR that should get created
            VmrPullRequestUrl = $"{VmrUri}/pulls/2";
            CreatePullRequestShouldReturnAValidValue();

            await WhenUpdateAssetsAsyncIsCalled(build2);

            var expectedState = new InProgressPullRequest()
            {
                UpdaterId = GetPullRequestUpdaterId(Subscription).Id,
                Url = VmrPullRequestUrl,
                HeadBranch = InProgressPrHeadBranch,
                HeadBranchSha = InProgressPrHeadBranchSha,
                SourceSha = build2.Commit,
                ContainedSubscriptions =
                [
                    new()
                    {
                        SubscriptionId = Subscription.Id,
                        BuildId = build2.Id,
                        SourceRepo = build.GetRepository(),
                        CommitSha = build2.Commit
                    }
                ],
                RequiredUpdates = [],
                CodeFlowDirection = CodeFlowDirection.ForwardFlow,
            };

            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(build2, expectedState: expectedState);
        }
    }

    // Tests a case when user decides to merge the PR manually instead of calling `darc resolve-conflicts`.
    // We must then make sure that the latest applied build for the subscriptions is updated accordingly.
    [Test]
    public async Task UpdateWithManuallyMergedPrAndLatestFlowNotApplied()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            });

        // The situation is following:
        // 1. build1 is flowed and PR is opened
        // 2. build2 is flowed but conflicts so it's not applied
        // 3. User manually merges the PR without resolving conflicts in the PR.
        //    This means build1 was applied but build2 was not.
        // 4. build2 is flowed again and a new PR should open (with a conflict inside)
        Build build1 = GivenANewBuild(true);
        build1.Id = 100;
        Build build2 = GivenANewBuild(true);
        build2.Id = 101;

        using (WithExistingCodeFlowPullRequest(build2, PrStatus.Merged, null, willFlowNewBuild: true))
        {
            // Original PR is merged, we should try to delete the branch
            DarcRemotes[VmrUri]
                .Setup(x => x.DeletePullRequestBranchAsync(VmrPullRequestUrl))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // When the build2 was not applied, we must find build1 metadata in the merged source manifest
            DarcRemotes[VmrUri]
                .Setup(x => x.GetSourceManifestAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new SourceManifest(
                    [
                        new RepositoryRecord(
                            Subscription.TargetDirectory,
                            build1.GetRepository(),
                            build1.Commit,
                            build1.Id),
                        new RepositoryRecord(
                            "another-repo",
                            "https://github.com/another/repo",
                            "abcdef01234234423",
                            build1.Id + 100),
                    ], []))
                .Verifiable();

            // URI of the new PR that should get created
            VmrPullRequestUrl = $"{VmrUri}/pulls/2";
            CreatePullRequestShouldReturnAValidValue();

            await WhenUpdateAssetsAsyncIsCalled(build2);

            Subscription.LastAppliedBuild.Id.Should().Be(build1.Id);

            var expectedState = new InProgressPullRequest()
            {
                UpdaterId = GetPullRequestUpdaterId(Subscription).Id,
                Url = VmrPullRequestUrl,
                HeadBranch = InProgressPrHeadBranch,
                HeadBranchSha = InProgressPrHeadBranchSha,
                SourceSha = build2.Commit,
                ContainedSubscriptions =
                [
                    new()
                    {
                        SubscriptionId = Subscription.Id,
                        BuildId = build2.Id,
                        SourceRepo = build1.GetRepository(),
                        CommitSha = build2.Commit,
                    }
                ],
                RequiredUpdates = [],
                CodeFlowDirection = CodeFlowDirection.ForwardFlow,
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
