// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib.Helpers;
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

        await WhenUpdateAssetsAsyncIsCalled(b, shouldGetUpdates: true);

        ThenGetRequiredUpdatesShouldHaveBeenCalled(b, false);
        AndCreateNewBranchShouldHaveBeenCalled();
        AndCommitUpdatesShouldHaveBeenCalled(b);
        AndCreatePullRequestShouldHaveBeenCalled();
        AndShouldHavePullRequestCheckReminder();
        AndShouldHaveInProgressPullRequestState(b, relativeBasePath: UnixPath.Empty);
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
            await WhenUpdateAssetsAsyncIsCalled(b, shouldGetUpdates: true);

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

        await WhenUpdateAssetsAsyncIsCalled(b, shouldGetUpdates: true);

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
            ],
            relativeBasePath: UnixPath.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithAssetsWithExcludedAssetsSomePackagesExcluded(bool batchable)
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            },
            excludedAssetPatterns: "quail.eating.*"); // This should exclude the "quail.eating.ducks" package

        // Create a build with multiple assets, some matching the exclusion pattern
        Build b = GivenANewBuild(true, [
            ("quail.eating.ducks", "1.1.0", false),      // Should be excluded
            ("quite.expensive.device", "2.0.1", true),   // Should NOT be excluded  
            ("another.package", "3.0.0", false)          // Should NOT be excluded
        ]);

        static bool assetFilter(Asset asset) => !asset.Name.StartsWith("quail.eating.");

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();

        CreatePullRequestShouldReturnAValidValue();

        await WhenUpdateAssetsAsyncIsCalled(b, shouldGetUpdates: true);

        ThenGetRequiredUpdatesShouldHaveBeenCalled(b, false, assetFilter);
        AndCreateNewBranchShouldHaveBeenCalled();
        AndCommitUpdatesShouldHaveBeenCalled(b, assetFilter);
        AndCreatePullRequestShouldHaveBeenCalled();
        AndShouldHavePullRequestCheckReminder();
        AndShouldHaveInProgressPullRequestState(b, assetFilter: assetFilter, relativeBasePath: UnixPath.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithAssetsWithExcludedAssetsAllPackagesExcluded(bool batchable)
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            },
            excludedAssetPatterns: "*"); // This should exclude ALL packages
        
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
        ThenGetRequiredUpdatesShouldHaveBeenCalled(b, false, asset => false);
        AndSubscriptionShouldBeUpdatedForMergedPullRequest(b);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateWithAssetsWithExcludedAssetsExistingPR(bool batchable)
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = batchable,
                UpdateFrequency = UpdateFrequency.EveryBuild
            },
            excludedAssetPatterns: "quail.*"); // Exclude packages starting with "quail"
        
        // Create a build with multiple assets
        Build b = GivenANewBuild(true, [
            ("quail.eating.ducks", "1.1.0", false),      // Should be excluded
            ("quite.expensive.device", "2.0.1", true),   // Should NOT be excluded
            ("quail.package", "1.5.0", false)            // Should be excluded
        ]);

        static bool assetFilter(Asset asset) => !asset.Name.StartsWith("quail.");

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();

        using (WithExistingPullRequest(b, canUpdate: true, assetFilter: assetFilter))
        {
            await WhenUpdateAssetsAsyncIsCalled(b, shouldGetUpdates: true);

            // Verify that only non-excluded assets were passed
            ThenGetRequiredUpdatesShouldHaveBeenCalled(b, true, assetFilter);
            AndCommitUpdatesShouldHaveBeenCalled(b, assetFilter);
            AndUpdatePullRequestShouldHaveBeenCalled();
            AndShouldHavePullRequestCheckReminder();
            AndShouldHaveInProgressPullRequestState(b, assetFilter: assetFilter);
        }
    }

    [Test]
    public async Task UpdateWithAssetsWithTargetDirectories()
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            },
            targetDirectory: ".,src/foo,src/bar");
        Build b = GivenANewBuild(true);
        UnixPath path1 = UnixPath.Empty;
        UnixPath path2 = new("src/foo");
        UnixPath path3 = new("src/bar");
        UnixPath[] targetDirectories = [path1, path2, path3];

        WithRequireNonCoherencyUpdates();
        WithNoRequiredCoherencyUpdates();
        CreatePullRequestShouldReturnAValidValue(TargetRepo);

        await WhenUpdateAssetsAsyncIsCalled(b, shouldGetUpdates: true);

        ThenGetRequiredUpdatesForMultipleDirectoriesShouldHaveBeenCalled(b, false, targetDirectories);
        AndCreateNewBranchShouldHaveBeenCalled();
        AndCommitUpdatesForMultipleDirectoriesShouldHaveBeenCalled(b, 3);
        AndCreatePullRequestShouldHaveBeenCalled();
        // create a list of dependency updates for every path and every asset in the build
        List<DependencyUpdateSummary> dependencyUpdates = b.Assets
            .SelectMany(a => targetDirectories.Select(dir => new DependencyUpdateSummary
            {
                DependencyName = a.Name,
                FromVersion = a.Version,
                ToVersion = a.Version,
                FromCommitSha = NewCommit,
                ToCommitSha = "sha3333",
                RelativeBasePath = dir
            }))
            .ToList();
        AndShouldHaveInProgressPullRequestState(b, url: VmrPullRequestUrl, dependencyUpdates: dependencyUpdates);
        AndShouldHavePullRequestCheckReminder(VmrPullRequestUrl);
    }
}
