// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [NonParallelizable]
    [Category("PostDeployment")]
    public class ScenarioTests_GitHubFlow : MaestroScenarioTestBase
    {
        private readonly IImmutableList<AssetData> source1Assets;
        private readonly IImmutableList<AssetData> source2Assets;
        private readonly IImmutableList<AssetData> source1AssetsUpdated;
        private readonly List<DependencyDetail> expectedDependenciesSource1;
        private readonly List<DependencyDetail> expectedDependenciesSource2;
        private readonly List<DependencyDetail> expectedDependenciesSource1Updated;

        public ScenarioTests_GitHubFlow()
        {
            using TestParameters parameters = TestParameters.GetAsync().Result;
            SetTestParameters(parameters);

            source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
            source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
            source1AssetsUpdated = GetAssetData("Foo", "1.17.0", "Bar", "2.17.0");

            expectedDependenciesSource1 = new List<DependencyDetail>();
            string sourceRepoUri = GetRepoUrl(TestRepository.TestRepo1Name);
            DependencyDetail foo = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1.Add(foo);

            DependencyDetail bar = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1.Add(bar);

            expectedDependenciesSource2 = new List<DependencyDetail>();
            string source2RepoUri = GetRepoUrl(TestRepository.TestRepo3Name);
            DependencyDetail pizza = new DependencyDetail
            {
                Name = "Pizza",
                Version = "3.1.0",
                RepoUri = source2RepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource2.Add(pizza);

            DependencyDetail hamburger = new DependencyDetail
            {
                Name = "Hamburger",
                Version = "4.1.0",
                RepoUri = source2RepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource2.Add(hamburger);

            expectedDependenciesSource1Updated = new List<DependencyDetail>();
            DependencyDetail fooUpdated = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1Updated.Add(fooUpdated);

            DependencyDetail barUpdated = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1Updated.Add(barUpdated);
        }

        [Test]
        public async Task Darc_GitHubFlow_Batched()
        {
            TestContext.WriteLine("Github Dependency Flow, batched");

            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);
            List<DependencyDetail> expectedDependencies = expectedDependenciesSource1.Concat(expectedDependenciesSource2).ToList();

            await testLogic.DarcBatchedFlowTestBase(
                $"GitHub_BatchedTestBranch_{Environment.MachineName}",
                $"GitHub Batched Channel {Environment.MachineName}",
                source1Assets,
                source2Assets,
                expectedDependencies,
                false).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched");

            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            await testLogic.NonBatchedUpdatingGitHubFlowTestBase(
                $"GitHub_NonBatchedTestBranch_{Environment.MachineName}",
                $"GitHub Non-Batched Channel {Environment.MachineName}",
                source1Assets,
                source1AssetsUpdated,
                expectedDependenciesSource1,
                expectedDependenciesSource1Updated).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_StrictCoherency()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched");

            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            List<DependencyDetail> expectedCoherencyDependencies = new List<DependencyDetail>
            {
                new DependencyDetail
                {
                    Name = "Foo",
                    Version = "1.1.0",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo1Name),
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = "Bar",
                    Version = "2.1.0",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo1Name),
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                }
            };

            IImmutableList<AssetData> sourceAssets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");

            await testLogic.NonBatchedGitHubFlowTestBase(
                $"GitHub_NonBatchedTestBranch_{Environment.MachineName}",
                $"GitHub Non-Batched Channel {Environment.MachineName}",
                sourceAssets,
                expectedCoherencyDependencies,
                allChecks: true).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_FailingCoherencyUpdate()
        {
            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            List<DependencyDetail> expectedCoherencyDependencies = new List<DependencyDetail>
            {
                new DependencyDetail
                {
                    Name = "Foo",
                    Version = "1.1.0",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo2Name),
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = "Bar",
                    Version = "2.1.0",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo2Name),
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = "Fzz",
                    Version = "",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo1Name),
                    Commit = "",
                    Type = DependencyType.Product,
                    CoherentParentDependencyName = "Foo"
                },
                new DependencyDetail
                {
                    Name = "ASD",
                    Version = "",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo1Name),
                    Commit = "",
                    Type = DependencyType.Product,
                    CoherentParentDependencyName = "Foo"
                },
            };

            IImmutableList<AssetData> sourceAssets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
            IImmutableList<AssetData> childSourceAssets = GetAssetData("Fzz", "1.1.0", "ASD", "1.1.1");

            await testLogic.NonBatchedGitHubFlowCoherencyTestBase(
                $"GitHub_NonBatchedTestBranch_FailingCoherencyUpdate_{Environment.MachineName}",
                $"GitHub Non-Batched Channel FailingCoherencyUpdate {Environment.MachineName}",
                sourceAssets,
                childSourceAssets,
                expectedCoherencyDependencies,
                coherentParent: "Foo",
                allChecks: false).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_FailingCoherentOnlyUpdate()
        {
            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            List<DependencyDetail> expectedNonCoherencyDependencies = new List<DependencyDetail>
            {
                new DependencyDetail
                {
                    Name = "A1",
                    Version = "1.1.0",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo2Name),
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = "A2",
                    Version = "1.1.0",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo2Name),
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                }
            };

            List<DependencyDetail> expectedCoherencyDependencies = new List<DependencyDetail>
            {
                new DependencyDetail
                {
                    Name = "A1",
                    Version = "1.1.0",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo2Name),
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = "A2",
                    Version = "1.1.0",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo2Name),
                    Commit = TestRepository.CoherencyTestRepo1Commit,
                    Type = DependencyType.Product,
                    Pinned = false
                },
                new DependencyDetail
                {
                    Name = "B1",
                    Version = "",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo1Name),
                    Commit = "",
                    Type = DependencyType.Product,
                    CoherentParentDependencyName = "A1"
                },
                new DependencyDetail
                {
                    Name = "B2",
                    Version = "",
                    RepoUri = GetRepoUrl(TestRepository.TestRepo1Name),
                    Commit = "",
                    Type = DependencyType.Product,
                    CoherentParentDependencyName = "A1"
                },
            };

            IImmutableList<AssetData> sourceAssets = GetAssetData("A1", "1.1.0", "A2", "1.1.0");
            IImmutableList<AssetData> childSourceAssets = GetAssetData("B1", "2.1.0", "B2", "2.1.0");

            await testLogic.NonBatchedGitHubFlowCoherencyOnlyTestBase(
                $"GitHub_NonBatchedTestBranch_FailingCoherencyOnlyUpdate_{Environment.MachineName}",
                $"GitHub Non-Batched Channel FailingCoherencyOnlyUpdate {Environment.MachineName}",
                sourceAssets,
                childSourceAssets,
                expectedNonCoherencyDependencies,
                expectedCoherencyDependencies,
                coherentParent: "A1").ConfigureAwait(false);
        }
    }
}
