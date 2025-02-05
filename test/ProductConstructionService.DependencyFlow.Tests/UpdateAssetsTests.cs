// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Maestro.MergePolicies;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture, NonParallelizable]
internal class UpdateAssetsTests : UpdateAssetsPullRequestUpdaterTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithAssetsNoExistingPR(bool batchable)
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b = GivenANewBuild(true);

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();

        CreatePullRequestShouldReturnAValidValue();

        await WhenUpdateAssetsAsyncIsCalled(b);

        ThenGetRequiredUpdatesShouldHaveBeenCalled(b, false);
        AndCreateNewBranchShouldHaveBeenCalled();
        AndCommitUpdatesShouldHaveBeenCalled(b);
        AndCreatePullRequestShouldHaveBeenCalled();
        AndShouldHavePullRequestCheckReminder();
        AndShouldHaveInProgressPullRequestState(b, nextCommitToProcess: null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithAssetsExistingPR(bool batchable)
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b = GivenANewBuild(true);

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();

        using (WithExistingPullRequest(b, canUpdate: true))
        {
            await WhenUpdateAssetsAsyncIsCalled(b);

            ThenGetRequiredUpdatesShouldHaveBeenCalled(b, true);
            AndCommitUpdatesShouldHaveBeenCalled(b);
            AndUpdatePullRequestShouldHaveBeenCalled();
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(b, nextCommitToProcess: null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithAssetsExistingPRNotUpdatable(bool batchable)
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b = GivenANewBuild(true);

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();
        using (WithExistingPullRequest(b, canUpdate: false))
        {
            await WhenUpdateAssetsAsyncIsCalled(b);

            ThenShouldHavePendingUpdateState(b);
            AndShouldHaveInProgressPullRequestState(b, nextCommitToProcess: b.Commit);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithNoAssets(bool batchable)
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b = GivenANewBuild(true, []);

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();

        await WhenUpdateAssetsAsyncIsCalled(b);

        ThenGetRequiredUpdatesShouldHaveBeenCalled(b, false);
        AndSubscriptionShouldBeUpdatedForMergedPullRequest(b);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithAssetsWhenStrictAlgorithmFails(bool batchable)
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b = GivenANewBuild(true);

        WithRequireNonCoherencyUpdates();
        WithFailsStrictCheckForCoherencyUpdates();

        CreatePullRequestShouldReturnAValidValue();

        await WhenUpdateAssetsAsyncIsCalled(b);

        ThenGetRequiredUpdatesShouldHaveBeenCalled(b, false);
        AndCreateNewBranchShouldHaveBeenCalled();
        AndCommitUpdatesShouldHaveBeenCalled(b);
        AndCreatePullRequestShouldHaveBeenCalled();
        AndShouldHavePullRequestCheckReminder();
        AndShouldHaveInProgressPullRequestState(
            b,
            nextCommitToProcess: null,
            coherencyCheckSuccessful: false,
            coherencyErrors: [
                new CoherencyErrorDetails()
                    {
                        Error = "Repo @ commit does not contain dependency fakeDependency",
                        PotentialSolutions = new List<string>()
                    }
            ]);
    }
}
