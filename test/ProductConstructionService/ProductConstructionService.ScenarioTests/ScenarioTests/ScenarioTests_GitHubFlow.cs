// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.Darc;
using NUnit.Framework;
using NUnit.Framework.Internal;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("GitHub")]
[Parallelizable]
internal class ScenarioTests_GitHubFlow : TestLogic
{
    private List<AssetData> _source1Assets = null;
    private List<AssetData> _source2Assets = null;
    private List<AssetData> _source1AssetsUpdated = null;
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

        List<AssetData> sourceAssets = GetAssetData(GetUniqueAssetName("Foo"), "1.1.0", GetUniqueAssetName("Bar"), "2.1.0");

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
                Version = "1.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = string.Empty,
                Type = DependencyType.Product,
                CoherentParentDependencyName = GetUniqueAssetName("Foo")
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("ASD"),
                Version = "1.1.1",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = string.Empty,
                Type = DependencyType.Product,
                CoherentParentDependencyName = GetUniqueAssetName("Foo")
            },
        ];

        List<AssetData> sourceAssets = GetAssetData(GetUniqueAssetName("Foo"), "1.1.0", GetUniqueAssetName("Bar"), "2.1.0");
        List<AssetData> childSourceAssets = GetAssetData(GetUniqueAssetName("Fzz"), "1.1.0", GetUniqueAssetName("ASD"), "1.1.1");

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
                Version = "2.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = string.Empty,
                Type = DependencyType.Product,
                CoherentParentDependencyName = GetUniqueAssetName("A1")
            },
            new DependencyDetail
            {
                Name = GetUniqueAssetName("B2"),
                Version = "2.1.0",
                RepoUri = GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                Commit = string.Empty,
                Type = DependencyType.Product,
                CoherentParentDependencyName = GetUniqueAssetName("A1")
            },
        ];

        List<AssetData> sourceAssets = GetAssetData(GetUniqueAssetName("A1"), "1.1.0", GetUniqueAssetName("A2"), "1.1.0");
        List<AssetData> childSourceAssets = GetAssetData(GetUniqueAssetName("B1"), "2.1.0", GetUniqueAssetName("B2"), "2.1.0");

        await NonBatchedGitHubFlowCoherencyOnlyTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            sourceAssets,
            childSourceAssets,
            expectedNonCoherencyDependencies,
            expectedCoherencyDependencies,
            coherentParent: GetUniqueAssetName("A1"));
    }

    [Test]
    public async Task Darc_GitHubFlow_SubWithTargetDirectories()
    {
        TestContext.WriteLine("GitHub Dependency Flow with Target Directories");

        List<AssetData> sourceAssets = GetAssetData(GetUniqueAssetName("VmrFoo"), "1.1.0", GetUniqueAssetName("VmrBar"), "2.1.0", GetUniqueAssetName("VmrBaz"), "3.1.0");
        List<AssetData> build1Assets = GetAssetData(GetUniqueAssetName("VmrFoo"), "1.1.1", GetUniqueAssetName("VmrBar"), "2.1.1", GetUniqueAssetName("VmrBaz"), "3.1.1");
        List<AssetData> build2Assets = GetAssetData(GetUniqueAssetName("VmrFoo"), "1.2.1", GetUniqueAssetName("VmrBar"), "2.2.1", GetUniqueAssetName("VmrBaz"), "3.2.1");

        var sourceRepoUri = GetGitHubRepoUrl(TestRepository.VmrTestRepoName);

        // Define expected dependencies by directory after the PR update
        Dictionary<string, List<DependencyDetail>> expectedDependenciesByDirectory1 = new()
        {
            ["src/maestro-test1"] = 
            [
                new DependencyDetail
                {
                    Name = GetUniqueAssetName("VmrFoo"),
                    Version = "1.1.1",
                    RepoUri = sourceRepoUri,
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = GetUniqueAssetName("VmrBar"),
                    Version = "2.1.1",
                    RepoUri = sourceRepoUri,
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = GetUniqueAssetName("VmrBaz"),
                    Version = "3.1.0",
                    RepoUri = sourceRepoUri,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = "Microsoft.DotNet.Arcade.Sdk",
                    Version = "1.0.0-beta.1.9.1.22.1",
                    RepoUri = "https://github.com/dotnet/arcade",
                    Commit = "44a0f58d1f465f9c35a24768cf44ba14811c8bfb",
                    Type = DependencyType.Toolset,
                    Pinned = false
                }
            ],
            ["src/maestro-test2"] = 
            [
                new DependencyDetail
                {
                    Name = GetUniqueAssetName("VmrBar"),
                    Version = "2.1.1",
                    RepoUri = sourceRepoUri,
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = GetUniqueAssetName("VmrBaz"),
                    Version = "3.1.0",
                    RepoUri = sourceRepoUri,
                    Type = DependencyType.Product,
                    Pinned = false
                }
            ]
        };
        Dictionary<string, List<DependencyDetail>> expectedDependenciesByDirectory2 = new()
        {
            ["src/maestro-test1"] =
            [
                new DependencyDetail
                {
                    Name = GetUniqueAssetName("VmrFoo"),
                    Version = "1.2.1",
                    RepoUri = sourceRepoUri,
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = GetUniqueAssetName("VmrBar"),
                    Version = "2.2.1",
                    RepoUri = sourceRepoUri,
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = GetUniqueAssetName("VmrBaz"),
                    Version = "3.1.0",
                    RepoUri = sourceRepoUri,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = "Microsoft.DotNet.Arcade.Sdk",
                    Version = "1.0.0-beta.1.9.1.22.1",
                    RepoUri = "https://github.com/dotnet/arcade",
                    Commit = "44a0f58d1f465f9c35a24768cf44ba14811c8bfb",
                    Type = DependencyType.Toolset,
                    Pinned = false
                }
            ],
            ["src/maestro-test2"] =
            [
                new DependencyDetail
                {
                    Name = GetUniqueAssetName("VmrBar"),
                    Version = "2.2.1",
                    RepoUri = sourceRepoUri,
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = GetUniqueAssetName("VmrBaz"),
                    Version = "3.1.0",
                    RepoUri = sourceRepoUri,
                    Type = DependencyType.Product,
                    Pinned = false
                }
            ]
        };

        await GitHubMultipleTargetDirectoriesTestBase(
            GetTestBranchName(),
            GetTestChannelName(),
            sourceAssets,
            build1Assets,
            build2Assets,
            expectedDependenciesByDirectory1,
            expectedDependenciesByDirectory2);
    }
}
