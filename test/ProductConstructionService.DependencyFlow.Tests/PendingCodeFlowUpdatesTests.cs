﻿// Licensed to the .NET Foundation under one or more agreements.
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
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build build = GivenANewBuild(true);

        AndPendingUpdates(build, isCodeFlow: true);

        using (WithExistingCodeFlowPullRequest(build, canUpdate: false))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(build, isCodeFlow: true);
            AndShouldHaveInProgressPullRequestState(build, build.Commit);
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

        using (WithExistingCodeFlowPullRequest(build, canUpdate: true))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(build, isCodeFlow: true);

            AndShouldHaveInProgressPullRequestState(build, nextCommitToProcess: null);
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
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha456";

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true))
        {
            ExpectPrMetadataToBeUpdated();

            await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true);

            ThenCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(newBuild, nextCommitToProcess: null);
        }
    }

    [Test]
    public async Task PendingUpdatesInConflictWithCurrent()
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
        newBuild.Commit = "sha456";

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, newChangeWillConflict: true))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true);

            ThenShouldHavePendingUpdateState(newBuild, isCodeFlow: true);
            AndShouldNotHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(
                oldBuild,
                nextCommitToProcess: newBuild.Commit,
                overwriteBuildCommit: ConflictPRRemoteSha,
                prState: InProgressPullRequestState.Conflict);
        }
    }

    [Test]
    public async Task PendingUpdateNotUpdatablePrAlreadyInConflict()
    {
        GivenATestChannel();
        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build build = GivenANewBuild(true);

        using (WithExistingCodeFlowPullRequest(build, canUpdate: true, prAlreadyHasConflict: true))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(build, isCodeFlow: true);

            ThenShouldHavePendingUpdateState(build, isCodeFlow: true);
            AndShouldNotHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(
                build,
                nextCommitToProcess: build.Commit,
                overwriteBuildCommit: ConflictPRRemoteSha,
                prState: InProgressPullRequestState.Conflict);
        }
    }


    [Test]
    public async Task PendingUpdateUpdatableConflictInPrResolved()
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
        newBuild.Commit = "sha456";
        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, prAlreadyHasConflict: true, latestCommitToReturn: "sha4"))
        {
            ExpectPrMetadataToBeUpdated();
            await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true);
            ThenCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(newBuild, nextCommitToProcess: null);
        }
    }
}
