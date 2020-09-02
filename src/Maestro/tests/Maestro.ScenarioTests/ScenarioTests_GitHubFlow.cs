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
        private readonly IImmutableList<AssetData> childSourceBuildAssets;
        private readonly IImmutableList<AssetData> childSourceAssets;
        private readonly List<DependencyDetail> expectedDependenciesSource1;
        private readonly List<DependencyDetail> expectedDependenciesSource2;
        private readonly List<DependencyDetail> expectedDependenciesSource1Updated;
        private readonly List<DependencyDetail> expectedCoherencyDependencies;

        public ScenarioTests_GitHubFlow()
        {
            using TestParameters parameters = TestParameters.GetAsync().Result;
            SetTestParameters(parameters);

            source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
            source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
            source1AssetsUpdated = GetAssetData("Foo", "1.17.0", "Bar", "2.17.0");
            childSourceBuildAssets = GetAssetData("Baz", "1.3.0", "Bop", "1.0");
            childSourceAssets = GetSingleAssetData("Baz", "1.3.0");

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

            expectedCoherencyDependencies = new List<DependencyDetail>();
            DependencyDetail baz = new DependencyDetail
            {
                Name = "Baz",
                Version = "1.3.0",
                RepoUri = GetRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo2Commit,
                Type = DependencyType.Product,
                Pinned = false,
                CoherentParentDependencyName = "Foo"
            };

            DependencyDetail parentFoo = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = GetRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };

            DependencyDetail parentBar = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = GetRepoUrl(TestRepository.TestRepo2Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };

            expectedCoherencyDependencies.Add(parentFoo);
            expectedCoherencyDependencies.Add(parentBar);
            expectedCoherencyDependencies.Add(baz);
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
        public async Task Darc_GitHubFlow_NonBatched_AllChecksSuccessful()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched, all checks successful");

            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            await testLogic.NonBatchedGitHubFlowTestBase(
                $"GitHub_NonBatchedTestBranch_AllChecks_{Environment.MachineName}", 
                $"GitHub Non-Batched All Checks Channel {Environment.MachineName}",
                source1Assets,
                expectedDependenciesSource1,
                allChecks: true).ConfigureAwait(false);
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
        public async Task Darc_GitHubFlow_NonBatched_WithCoherency()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched");

            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            await testLogic.NonBatchedGitHubFlowTestBase(
                $"GitHub_NonBatchedTestCoherencyBranch_{Environment.MachineName}", 
                $"GitHub Non-Batched Coherency Channel {Environment.MachineName}", 
                source1Assets, 
                expectedDependenciesSource1,            
                isCoherencyTest: true,
                childSourceAssets : childSourceAssets,
                childSourceBuildAssets: childSourceBuildAssets).ConfigureAwait(false);
        }
    }
}
