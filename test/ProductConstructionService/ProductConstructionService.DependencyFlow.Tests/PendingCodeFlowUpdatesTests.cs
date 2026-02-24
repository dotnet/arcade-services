// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.Model;

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
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build build = GivenANewBuild(true);

        using (WithExistingCodeFlowPullRequest(build, canUpdate: true))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(build, isCodeFlow: true);

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
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha123456";

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, willFlowNewBuild: true))
        {
            ExpectPrMetadataToBeUpdated();

            await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true);

            ThenCodeShouldHaveBeenFlownForward(newBuild);
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
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha12345";

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, blockedFromFutureUpdates: true))
        {
            await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true);

            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
        }
    }

    [Test]
    public async Task ForcedPendingUpdatesUpdatableBlockedPr()
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
        newBuild.Commit = "sha12345";

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, blockedFromFutureUpdates: true, willFlowNewBuild: true))
        {
            ExpectPrMetadataToBeUpdated();

            await WhenProcessPendingUpdatesAsyncIsCalled(newBuild, isCodeFlow: true, forceUpdate: true);

            ThenCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHaveNoPendingUpdateState();
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(newBuild);
        }
    }
}
