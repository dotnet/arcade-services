// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Microsoft.DotNet.DarcLib.Models.Darc;
using NUnit.Framework;
using NUnit.Framework.Internal;
using ProductConstructionService.Client.Models;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("GitHub")]
[Parallelizable]
internal class ScenarioTests_GitHubFlow : TestLogic
{
    private IImmutableList<AssetData> _source1Assets = null;
    private IImmutableList<AssetData> _source2Assets = null;
    private IImmutableList<AssetData> _source1AssetsUpdated = null;
    private List<DependencyDetail> _expectedDependenciesSource1 = null;
    private List<DependencyDetail> _expectedDependenciesSource2 = null;
    private List<DependencyDetail> _expectedDependenciesSource1Updated = null;

    [SetUp]
    public void SetUp()
    {
        _source1Assets = GetAssetData(GetUniqueAssetName("Foo"), "1.1.0", GetUniqueAssetName("Bar"), "2.1.0");
        _source2Assets = GetAssetData(GetUniqueAssetName("Pizza"), "3.1.0", GetUniqueAssetName("Hamburger"), "4.1.0");
        _source1AssetsUpdated = GetAssetData(GetUniqueAssetName("Foo"), "1.17.0", GetUniqueAssetName("Bar"), "2.17.0");

        var sourceRepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var source2RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo3Name);

        _expectedDependenciesSource1 =
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
            }
        ];

        _expectedDependenciesSource2 =
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
            }
        ];

        _expectedDependenciesSource1Updated =
        [
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Foo"),
                Version = "1.17.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo2Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Bar"),
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

        var expectedDependencies = _expectedDependenciesSource1.Concat(_expectedDependenciesSource2).ToList();

        await DarcBatchedFlowTestBase(
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

        await NonBatchedUpdatingGitHubFlowTestBase(
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

        List<DependencyDetail> expectedCoherencyDependencies =
        [
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Foo"),
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Bar"),
                Version = "2.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            }
        ];

        IImmutableList<AssetData> sourceAssets = GetAssetData(GetUniqueAssetName("Foo"), "1.1.0", GetUniqueAssetName("Bar"), "2.1.0");

        await NonBatchedGitHubFlowTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            sourceAssets,
            expectedCoherencyDependencies,
            allChecks: true);
    }

    [Test]
    public async Task Darc_GitHubFlow_NonBatched_FailingCoherencyUpdate()
    {
        List<DependencyDetail> expectedCoherencyDependencies =
        [
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Foo"),
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Bar"),
                Version = "2.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("Fzz"),
                Version = string.Empty,
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = string.Empty,
                Type = DependencyType.Product,
                CoherentParentDependencyName = GetUniqueAssetName("Foo")
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("ASD"),
                Version = string.Empty,
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = string.Empty,
                Type = DependencyType.Product,
                CoherentParentDependencyName = GetUniqueAssetName("Foo")
            },
        ];

        IImmutableList<AssetData> sourceAssets = GetAssetData(GetUniqueAssetName("Foo"), "1.1.0", GetUniqueAssetName("Bar"), "2.1.0");
        IImmutableList<AssetData> childSourceAssets = GetAssetData(GetUniqueAssetName("Fzz"), "1.1.0", GetUniqueAssetName("ASD"), "1.1.1");

        await NonBatchedGitHubFlowCoherencyTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            sourceAssets,
            childSourceAssets,
            expectedCoherencyDependencies,
            coherentParent: GetUniqueAssetName("Foo"),
            allChecks: false);
    }

    [Test]
    public async Task Darc_GitHubFlow_NonBatched_FailingCoherentOnlyUpdate()
    {
        List<DependencyDetail> expectedNonCoherencyDependencies =
        [
            new DependencyDetail
            {
                Name = GetUniqueAssetName("A1"),
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("A2"),
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
                Name = GetUniqueAssetName("A1"),
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("A2"),
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("B1"),
                Version = string.Empty,
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = string.Empty,
                Type = DependencyType.Product,
                CoherentParentDependencyName = GetUniqueAssetName("A1")
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("B2"),
                Version = string.Empty,
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = string.Empty,
                Type = DependencyType.Product,
                CoherentParentDependencyName = GetUniqueAssetName("A1")
            },
        ];

        IImmutableList<AssetData> sourceAssets = GetAssetData(GetUniqueAssetName("A1"), "1.1.0", GetUniqueAssetName("A2"), "1.1.0");
        IImmutableList<AssetData> childSourceAssets = GetAssetData(GetUniqueAssetName("B1"), "2.1.0", GetUniqueAssetName("B2"), "2.1.0");

        await NonBatchedGitHubFlowCoherencyOnlyTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            sourceAssets,
            childSourceAssets,
            expectedNonCoherencyDependencies,
            expectedCoherencyDependencies,
            coherentParent: GetUniqueAssetName("A1"));
    }
}
