// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Microsoft.DotNet.DarcLib;
using NUnit.Framework;
using ProductConstructionService.Client.Models;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("AzDO")]
[NonParallelizable]
internal class ScenarioTests_AzDoFlow : ScenarioTestBase
{
    private readonly IImmutableList<AssetData> _source1Assets;
    private readonly IImmutableList<AssetData> _source2Assets;
    private readonly IImmutableList<AssetData> _source1AssetsUpdated;
    private readonly List<DependencyDetail> _expectedAzDoDependenciesSource1;
    private readonly List<DependencyDetail> _expectedAzDoDependenciesSource2;
    private readonly List<DependencyDetail> _expectedAzDoDependenciesSource1Updated;

    public ScenarioTests_AzDoFlow()
    {
        _source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
        _source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
        _source1AssetsUpdated = GetAssetData("Foo", "1.17.0", "Bar", "2.17.0");

        var sourceRepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name);
        var source2RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo3Name);

        _expectedAzDoDependenciesSource1 =
        [
            new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = "Bar",
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
                Name = "Pizza",
                Version = "3.1.0",
                RepoUri = source2RepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = "Hamburger",
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
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = "Bar",
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

        TestParameters parameters = await TestParameters.GetAsync(useNonPrimaryEndpoint: true);
        SetTestParameters(parameters);
        var testLogic = new EndToEndFlowLogic(parameters);
        var expectedDependencies = _expectedAzDoDependenciesSource1.Concat(_expectedAzDoDependenciesSource2).ToList();

        await testLogic.DarcBatchedFlowTestBase(
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

        TestParameters parameters = await TestParameters.GetAsync();
        SetTestParameters(parameters);
        var testLogic = new EndToEndFlowLogic(parameters);

        await testLogic.NonBatchedAzDoFlowTestBase(
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

        TestParameters parameters = await TestParameters.GetAsync();
        SetTestParameters(parameters);
        var testLogic = new EndToEndFlowLogic(parameters);

        await testLogic.NonBatchedUpdatingAzDoFlowTestBase(
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

        IImmutableList<AssetData> feedFlowSourceAssets =
        [
            GetAssetDataWithLocations("Foo", "1.1.0", proxyFeed, LocationType.NugetFeed),
            GetAssetDataWithLocations("Bar", "2.1.0", azdoFeed1, LocationType.NugetFeed),
            GetAssetDataWithLocations("Pizza", "3.1.0", azdoFeed2, LocationType.NugetFeed, regularFeed, LocationType.NugetFeed),
            GetAssetDataWithLocations("Hamburger", "4.1.0", azdoFeed3, LocationType.NugetFeed, buildContainer, LocationType.Container),
        ];

        TestContext.WriteLine("Azure DevOps Internal feed flow");
        TestParameters parameters = await TestParameters.GetAsync();
        SetTestParameters(parameters);

        var testLogic = new EndToEndFlowLogic(parameters);

        List<DependencyDetail> expectedAzDoFeedFlowDependencies =
        [
            new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = [proxyFeed],
            },

            new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = [azdoFeed1],
            },

            new DependencyDetail
            {
                Name = "Pizza",
                Version = "3.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = [azdoFeed2, regularFeed],
            },

            new DependencyDetail
            {
                Name = "Hamburger",
                Version = "4.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = [azdoFeed3, buildContainer],
            },
        ];

        await testLogic.NonBatchedAzDoFlowTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            feedFlowSourceAssets,
            expectedAzDoFeedFlowDependencies,
            isFeedTest: true,
            expectedFeeds: expectedFeeds,
            notExpectedFeeds: notExpectedFeeds);
    }
}
