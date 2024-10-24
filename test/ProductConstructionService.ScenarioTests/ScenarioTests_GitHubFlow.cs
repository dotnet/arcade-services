// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Microsoft.DotNet.DarcLib;
using NUnit.Framework;
using NUnit.Framework.Internal;
using ProductConstructionService.Client.Models;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("GitHub")]
[Parallelizable]
internal class ScenarioTests_GitHubFlow : ScenarioTestBase
{
    private readonly IImmutableList<AssetData> _source1Assets;
    private readonly IImmutableList<AssetData> _source2Assets;
    private readonly IImmutableList<AssetData> _source1AssetsUpdated;
    private readonly List<DependencyDetail> _expectedDependenciesSource1;
    private readonly List<DependencyDetail> _expectedDependenciesSource2;
    private readonly List<DependencyDetail> _expectedDependenciesSource1Updated;

    public ScenarioTests_GitHubFlow()
    {
        using TestParameters parameters = TestParameters.GetAsync().Result;
        SetTestParameters(parameters);

        _source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
        _source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
        _source1AssetsUpdated = GetAssetData("Foo", "1.17.0", "Bar", "2.17.0");

        var sourceRepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var source2RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo3Name);

        _expectedDependenciesSource1 =
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
            }
        ];

        _expectedDependenciesSource2 =
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
            }
        ];

        _expectedDependenciesSource1Updated =
        [
            new DependencyDetail
            {
                Name = "Foo",
                Version = "1.17.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo2Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = "Bar",
                Version = "2.17.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo2Commit,
                Type = DependencyType.Product,
                Pinned = false
            }
        ];
    }

    [Test]
    public async Task Darc_GitHubFlow_Batched()
    {
        TestContext.WriteLine("Github Dependency Flow, batched");

        using TestParameters parameters = await TestParameters.GetAsync();
        var testLogic = new EndToEndFlowLogic(parameters);
        var expectedDependencies = _expectedDependenciesSource1.Concat(_expectedDependenciesSource2).ToList();

        await testLogic.DarcBatchedFlowTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            _source1Assets,
            _source2Assets,
            expectedDependencies,
            false);
    }

    [Test]
    public async Task Darc_GitHubFlow_NonBatched()
    {
        TestContext.WriteLine("GitHub Dependency Flow, non-batched");

        using TestParameters parameters = await TestParameters.GetAsync();
        var testLogic = new EndToEndFlowLogic(parameters);

        await testLogic.NonBatchedUpdatingGitHubFlowTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            _source1Assets,
            _source1AssetsUpdated,
            _expectedDependenciesSource1,
            _expectedDependenciesSource1Updated);
    }

    [Test]
    public async Task Darc_GitHubFlow_NonBatched_StrictCoherency()
    {
        TestContext.WriteLine("GitHub Dependency Flow, non-batched");

        using TestParameters parameters = await TestParameters.GetAsync();
        var testLogic = new EndToEndFlowLogic(parameters);

        List<DependencyDetail> expectedCoherencyDependencies =
        [
            new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            }
        ];

        IImmutableList<AssetData> sourceAssets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");

        await testLogic.NonBatchedGitHubFlowTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            sourceAssets,
            expectedCoherencyDependencies,
            allChecks: true);
    }

    [Test]
    public async Task Darc_GitHubFlow_NonBatched_FailingCoherencyUpdate()
    {
        using TestParameters parameters = await TestParameters.GetAsync();
        var testLogic = new EndToEndFlowLogic(parameters);

        List<DependencyDetail> expectedCoherencyDependencies =
        [
            new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = "Fzz",
                Version = "",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = "",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "Foo"
            },
            new DependencyDetail
            {
                Name = "ASD",
                Version = "",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = "",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "Foo"
            },
        ];

        IImmutableList<AssetData> sourceAssets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
        IImmutableList<AssetData> childSourceAssets = GetAssetData("Fzz", "1.1.0", "ASD", "1.1.1");

        await testLogic.NonBatchedGitHubFlowCoherencyTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            sourceAssets,
            childSourceAssets,
            expectedCoherencyDependencies,
            coherentParent: "Foo",
            allChecks: false);
    }

    [Test]
    public async Task Darc_GitHubFlow_NonBatched_FailingCoherentOnlyUpdate()
    {
        using TestParameters parameters = await TestParameters.GetAsync();
        var testLogic = new EndToEndFlowLogic(parameters);

        List<DependencyDetail> expectedNonCoherencyDependencies =
        [
            new DependencyDetail
            {
                Name = "A1",
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = "A2",
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            }
        ];

        List<DependencyDetail> expectedCoherencyDependencies =
        [
            new DependencyDetail
            {
                Name = "A1",
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = "A2",
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = "B1",
                Version = "",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = "",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "A1"
            },
            new DependencyDetail
            {
                Name = "B2",
                Version = "",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = "",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "A1"
            },
        ];

        IImmutableList<AssetData> sourceAssets = GetAssetData("A1", "1.1.0", "A2", "1.1.0");
        IImmutableList<AssetData> childSourceAssets = GetAssetData("B1", "2.1.0", "B2", "2.1.0");

        await testLogic.NonBatchedGitHubFlowCoherencyOnlyTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            sourceAssets,
            childSourceAssets,
            expectedNonCoherencyDependencies,
            expectedCoherencyDependencies,
            coherentParent: "A1");
    }
}
