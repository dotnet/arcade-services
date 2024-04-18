// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data.Models;
using NUnit.Framework;
using SubscriptionActorService.StateModel;
using Asset = Maestro.Contracts.Asset;

namespace SubscriptionActorService.Tests;

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

        ExpectPcsToGetCalled(build);

        await WhenUpdateAssetsAsyncIsCalled(build);

        ThenShouldHavePendingUpdateState(build);
        AndPcsShouldHaveBeenCalled(build, prUrl: null, out var requestedBranch);
        AndShouldHaveCodeFlowState(build, requestedBranch);
        AndShouldHaveFollowingState(
            pullRequestUpdateState: true,
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
            pullRequestUpdateState: true,
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
        AndShouldHavePullRequestCheckReminder();
        AndShouldHaveInProgressCodeFlowPullRequestState(build);
        AndDependencyFlowEventsShouldBeAdded();
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

        using (WithExistingCodeFlowPullRequest(SynchronizePullRequestResult.InProgressCannotUpdate))
        {
            await WhenUpdateAssetsAsyncIsCalled(build);

            ThenShouldHavePendingUpdateState(build);
            ThenPcsShouldNotHaveBeenCalled(build, InProgressPrUrl);
            AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
            AndShouldHaveFollowingState(
                codeFlowState: true,
                pullRequestState: true,
                pullRequestUpdateState: true,
                pullRequestUpdateReminder: true);
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

        GivenAPullRequestCheckReminder();
        WithExistingCodeFlowStatus(build);
        WithExistingPrBranch();

        using (WithExistingCodeFlowPullRequest(SynchronizePullRequestResult.InProgressCanUpdate))
        {
            await WhenUpdateAssetsAsyncIsCalled(build);

            ThenPcsShouldNotHaveBeenCalled(build, InProgressPrUrl);
            AndShouldHaveCodeFlowState(build, InProgressPrHeadBranch);
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveFollowingState(
                codeFlowState: true,
                pullRequestState: true,
                pullRequestCheckReminder: true);
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

        GivenAPullRequestCheckReminder();
        WithExistingCodeFlowStatus(oldBuild);
        WithExistingPrBranch();
        ExpectPcsToGetCalled(newBuild);

        using (WithExistingCodeFlowPullRequest(SynchronizePullRequestResult.InProgressCanUpdate))
        {
            await WhenUpdateAssetsAsyncIsCalled(newBuild);

            ThenPcsShouldHaveBeenCalled(newBuild, InProgressPrUrl, out _);
            AndShouldHaveCodeFlowState(newBuild, InProgressPrHeadBranch);
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveFollowingState(
                codeFlowState: true,
                pullRequestState: true,
                pullRequestCheckReminder: true);
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
                var updates = new List<UpdateAssetsParameters>
                {
                    new()
                    {
                        SubscriptionId = Subscription.Id,
                        BuildId = forBuild.Id,
                        SourceRepo = forBuild.GitHubRepository ?? forBuild.AzureDevOpsRepository,
                        SourceSha = forBuild.Commit,
                        Assets = forBuild.Assets
                            .Select(a => new Asset {Name = a.Name, Version = a.Version})
                            .ToList(),
                        IsCoherencyUpdate = false,
                        IsCodeFlow = true,
                    }
                };

                var reminder = new MockReminderManager.Reminder(
                    PullRequestActorImplementation.PullRequestUpdateKey,
                    null,
                    TimeSpan.FromMinutes(3),
                    TimeSpan.FromMinutes(3));

                StateManager.Data[PullRequestActorImplementation.PullRequestUpdateKey] = updates;
                Reminders.Data[PullRequestActorImplementation.PullRequestUpdateKey] = reminder;
            });
    }
}
