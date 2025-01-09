// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.Darc;
using NUnit.Framework;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("AzDO")]
[NonParallelizable]
internal class ScenarioTests_AzDoFlow : TestLogic
{
    private List<AssetData> _source1Assets = null;
    private List<AssetData> _source2Assets = null;
    private List<AssetData> _source1AssetsUpdated = null;
    private List<DependencyDetail> _expectedAzDoDependenciesSource1 = null;
    private List<DependencyDetail> _expectedAzDoDependenciesSource2 = null;
    private List<DependencyDetail> _expectedAzDoDependenciesSource1Updated = null;

    [SetUp]
    public void SetUp()
    {
        _source1Assets = GetAssetData(GetUniqueAssetName("Foo"), "1.1.0", GetUniqueAssetName("Bar"), "2.1.0");
        _source2Assets = GetAssetData(GetUniqueAssetName("Pizza"), "3.1.0", GetUniqueAssetName("Hamburger"), "4.1.0");
        _source1AssetsUpdated = GetAssetData(GetUniqueAssetName("Foo"), "1.17.0", GetUniqueAssetName("Bar"), "2.17.0");

        var sourceRepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name);
        var source2RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo3Name);

        _expectedAzDoDependenciesSource1 =
        [
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Foo"),
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Bar"),
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
        ];

        _expectedAzDoDependenciesSource2 =
        [
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Pizza"),
                Version = "3.1.0",
                RepoUri = source2RepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Hamburger"),
                Version = "4.1.0",
                RepoUri = source2RepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
        ];

        _expectedAzDoDependenciesSource1Updated =
        [
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Foo"),
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Bar"),
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
        ];
    }

    [Test]
    public async Task Darc_AzDoFlow_Batched()
    {
        TestContext.WriteLine("Azure DevOps Dependency Flow, batched");

        var expectedDependencies = _expectedAzDoDependenciesSource1.Concat(_expectedAzDoDependenciesSource2).ToList();

        await DarcBatchedFlowTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            _source1Assets,
            _source2Assets,
            expectedDependencies,
            true);
    }

    [Test]
    public async Task Darc_AzDoFlow_NonBatched_AllChecksSuccessful()
    {
        TestContext.WriteLine("AzDo Dependency Flow, non-batched, all checks successful");

        await NonBatchedAzDoFlowTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            _source1Assets,
            _expectedAzDoDependenciesSource1,
            allChecks: true).ConfigureAwait(true);
    }

    [Test]
    public async Task Darc_AzDoFlow_NonBatched()
    {
        TestContext.WriteLine("AzDo Dependency Flow, non-batched");

        await NonBatchedUpdatingAzDoFlowTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            _source1Assets,
            _source1AssetsUpdated,
            _expectedAzDoDependenciesSource1,
            _expectedAzDoDependenciesSource1Updated);
    }

    [Test]
    public async Task Darc_AzDoFlow_FeedFlow()
    {
        TestContext.WriteLine("AzDo Dependency Feed Flow, non-batched");

        // Feed flow test strings
        var proxyFeed = "https://some-proxy.azurewebsites.net/container/some-container/sig/somesig/se/2020-02-02/darc-int-maestro-test1-bababababab-1/index.json";
        var azdoFeed1 = "https://some_org.pkgs.visualstudio.com/_packaging/darc-int-maestro-test1-aaaaaaaaaaaaaa-1/nuget/v3/index.json";
        var azdoFeed2 = "https://some_org.pkgs.visualstudio.com/_packaging/darc-int-maestro-test1-bbbbbbbbbbbbbb-1/nuget/v3/index.json";
        var azdoFeed3 = "https://some_org.pkgs.visualstudio.com/_packaging/darc-int-maestro-test1-cccccccccccccc-1/nuget/v3/index.json";
        var regularFeed = "https://dotnetfeed.blob.core.windows.net/maestro-test1/index.json";
        var buildContainer = "https://dev.azure.com/dnceng/internal/_apis/build/builds/9999999/artifacts";
        string[] expectedFeeds = [proxyFeed, azdoFeed1, azdoFeed3];
        string[] notExpectedFeeds = [regularFeed, azdoFeed2, buildContainer];

        List<AssetData> feedFlowSourceAssets =
        [
            GetAssetDataWithLocations(GetUniqueAssetName("Foo"), "1.1.0", proxyFeed, LocationType.NugetFeed),
            GetAssetDataWithLocations(GetUniqueAssetName("Bar"), "2.1.0", azdoFeed1, LocationType.NugetFeed),
            GetAssetDataWithLocations(GetUniqueAssetName("Pizza"), "3.1.0", azdoFeed2, LocationType.NugetFeed, regularFeed, LocationType.NugetFeed),
            GetAssetDataWithLocations(GetUniqueAssetName("Hamburger"), "4.1.0", azdoFeed3, LocationType.NugetFeed, buildContainer, LocationType.Container),
        ];

        TestContext.WriteLine("Azure DevOps Internal feed flow");

        List<DependencyDetail> expectedAzDoFeedFlowDependencies =
        [
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Foo"),
                Version = "1.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = [proxyFeed],
            },

            new DependencyDetail
            {
                Name = GetUniqueAssetName("Bar"),
                Version = "2.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = [azdoFeed1],
            },

            new DependencyDetail
            {
                Name = GetUniqueAssetName("Pizza"),
                Version = "3.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = [azdoFeed2, regularFeed],
            },

            new DependencyDetail
            {
                Name = GetUniqueAssetName("Hamburger"),
                Version = "4.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = [azdoFeed3, buildContainer],
            },
        ];

        await NonBatchedAzDoFlowTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            feedFlowSourceAssets,
            expectedAzDoFeedFlowDependencies,
            isFeedTest: true,
            expectedFeeds: expectedFeeds,
            notExpectedFeeds: notExpectedFeeds);
    }
}
