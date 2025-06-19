// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Maestro.MergePolicies;
using NUnit.Framework;
using System;

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
        AndShouldHaveInProgressPullRequestState(b);
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
            AndShouldHaveInProgressPullRequestState(b);
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
            AndShouldHaveInProgressPullRequestState(b, nextBuildToProcess: b.Id);
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
            coherencyCheckSuccessful: false,
            coherencyErrors: [
                new CoherencyErrorDetails()
                    {
                        Error = "Repo @ commit does not contain dependency fakeDependency",
                        PotentialSolutions = new List<string>()
                    }
            ]);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithAssetsWithExcludedAssetsSomePackagesExcluded(bool batchable)
    {
        GivenATestChannel();
        GivenASubscriptionWithExcludedAssets(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            },
            "quail.eating.*"); // This should exclude the "quail.eating.ducks" package
        
        // Create a build with multiple assets, some matching the exclusion pattern
        Build b = GivenANewBuild(true, [
            ("quail.eating.ducks", "1.1.0", false),      // Should be excluded
            ("quite.expensive.device", "2.0.1", true),   // Should NOT be excluded  
            ("another.package", "3.0.0", false)          // Should NOT be excluded
        ]);

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();

        CreatePullRequestShouldReturnAValidValue();

        await WhenUpdateAssetsAsyncIsCalled(b);

        // Verify that only non-excluded assets were passed to GetRequiredNonCoherencyUpdates
        ThenGetRequiredUpdatesShouldHaveBeenCalledWithFilteredAssets(b, false, 
            asset => !asset.Name.StartsWith("quail.eating."));
        AndCreateNewBranchShouldHaveBeenCalled();
        AndCommitUpdatesShouldHaveBeenCalledWithFilteredAssets(b, 
            asset => !asset.Name.StartsWith("quail.eating."));
        AndCreatePullRequestShouldHaveBeenCalled();
        AndShouldHavePullRequestCheckReminder();
        AndShouldHaveInProgressPullRequestStateWithFilteredAssets(b, 
            asset => !asset.Name.StartsWith("quail.eating."));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithAssetsWithExcludedAssetsAllPackagesExcluded(bool batchable)
    {
        GivenATestChannel();
        GivenASubscriptionWithExcludedAssets(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            },
            "*"); // This should exclude ALL packages
        
        // Create a build with assets - all should be excluded
        Build b = GivenANewBuild(true, [
            ("quail.eating.ducks", "1.1.0", false),
            ("quite.expensive.device", "2.0.1", true),
            ("another.package", "3.0.0", false)
        ]);

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();

        await WhenUpdateAssetsAsyncIsCalled(b);

        // Verify that no assets were passed to GetRequiredNonCoherencyUpdates (all excluded)
        ThenGetRequiredUpdatesShouldHaveBeenCalledWithFilteredAssets(b, false, 
            asset => false); // No assets should match
        AndSubscriptionShouldBeUpdatedForMergedPullRequest(b);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithAssetsWithExcludedAssetsExistingPR(bool batchable)
    {
        GivenATestChannel();
        GivenASubscriptionWithExcludedAssets(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            },
            "quail.*"); // Exclude packages starting with "quail"
        
        // Create a build with multiple assets
        Build b = GivenANewBuild(true, [
            ("quail.eating.ducks", "1.1.0", false),      // Should be excluded
            ("quite.expensive.device", "2.0.1", true),   // Should NOT be excluded
            ("quail.package", "1.5.0", false)            // Should be excluded
        ]);

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();

        using (WithExistingPullRequestWithFilteredAssets(b, asset => !asset.Name.StartsWith("quail"), canUpdate: true))
        {
            await WhenUpdateAssetsAsyncIsCalled(b);

            // Verify that only non-excluded assets were passed
            ThenGetRequiredUpdatesShouldHaveBeenCalledWithFilteredAssets(b, true, 
                asset => !asset.Name.StartsWith("quail"));
            AndCommitUpdatesShouldHaveBeenCalledWithFilteredAssets(b,
                asset => !asset.Name.StartsWith("quail"));
            AndUpdatePullRequestShouldHaveBeenCalled();
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestStateWithFilteredAssets(b,
                asset => !asset.Name.StartsWith("quail"));
        }
    }
}
