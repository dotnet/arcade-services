using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [NonParallelizable]
    [Category("PostDeployment")]
    class ScenarioTests_AzDoFlow : MaestroScenarioTestBase
    {
        private readonly IImmutableList<AssetData> source1Assets;
        private readonly IImmutableList<AssetData> source2Assets;
        private readonly IImmutableList<AssetData> source1AssetsUpdated;
        private readonly List<DependencyDetail> expectedAzDoDependenciesSource1;
        private readonly List<DependencyDetail> expectedAzDoDependenciesSource2;
        private readonly List<DependencyDetail> expectedAzDoDependenciesSource1Updated;

        public ScenarioTests_AzDoFlow()
        {
            source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
            source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
            source1AssetsUpdated = GetAssetData("Foo", "1.17.0", "Bar", "2.17.0");

            expectedAzDoDependenciesSource1 = new List<DependencyDetail>();
            string sourceRepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name);
            DependencyDetail foo = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedAzDoDependenciesSource1.Add(foo);

            DependencyDetail bar = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedAzDoDependenciesSource1.Add(bar);

            expectedAzDoDependenciesSource2 = new List<DependencyDetail>();
            string source2RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo3Name);
            DependencyDetail pizza = new DependencyDetail
            {
                Name = "Pizza",
                Version = "3.1.0",
                RepoUri = source2RepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedAzDoDependenciesSource2.Add(pizza);

            DependencyDetail hamburger = new DependencyDetail
            {
                Name = "Hamburger",
                Version = "4.1.0",
                RepoUri = source2RepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedAzDoDependenciesSource2.Add(hamburger);

            expectedAzDoDependenciesSource1Updated = new List<DependencyDetail>();
            DependencyDetail fooUpdated = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedAzDoDependenciesSource1Updated.Add(fooUpdated);

            DependencyDetail barUpdated = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedAzDoDependenciesSource1Updated.Add(barUpdated);
        }

        [Test]
        public async Task Darc_AzDoFlow_Batched()
        {
            TestContext.WriteLine("Azure DevOps Dependency Flow, batched");

            TestParameters parameters = await TestParameters.GetAsync();
            SetTestParameters(parameters);
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);
            List<DependencyDetail> expectedDependencies = expectedAzDoDependenciesSource1.Concat(expectedAzDoDependenciesSource2).ToList();

            await testLogic.DarcBatchedFlowTestBase(
                $"AzDo_BatchedTestBranch_{Environment.MachineName}",
                $"AzDo Batched Channel {Environment.MachineName}",
                source1Assets,
                source2Assets,
                expectedDependencies,
                true);
        }

        [Test]
        public async Task Darc_AzDoFlow_NonBatched_AllChecksSuccessful()
        {
            TestContext.WriteLine("AzDo Dependency Flow, non-batched, all checks successful");

            TestParameters parameters = await TestParameters.GetAsync();
            SetTestParameters(parameters);
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            await testLogic.NonBatchedAzDoFlowTestBase(
                $"AzDo_NonBatchedTestBranch_AllChecks_{Environment.MachineName}",
                $"AzDo Non-Batched All Checks Channel {Environment.MachineName}",
                source1Assets,
                expectedAzDoDependenciesSource1,
                allChecks: true).ConfigureAwait(true);
        }

        [Test]
        public async Task Darc_AzDoFlow_NonBatched()
        {
            TestContext.WriteLine("AzDo Dependency Flow, non-batched");

            TestParameters parameters = await TestParameters.GetAsync();
            SetTestParameters(parameters);
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            await testLogic.NonBatchedUpdatingAzDoFlowTestBase(
                $"AzDo_NonBatchedTestBranch_{Environment.MachineName}",
                $"AzDo Non-Batched Channel {Environment.MachineName}",
                source1Assets,
                source1AssetsUpdated,
                expectedAzDoDependenciesSource1,
                expectedAzDoDependenciesSource1Updated).ConfigureAwait(false);
        }

        [Test]
        [Ignore("https://github.com/dotnet/core-eng/issues/10688")]
        public async Task Darc_AzDoFlow_FeedFlow()
        {
            TestContext.WriteLine("AzDo Dependency Feed Flow, non-batched");

            // Feed flow test strings
            string proxyFeed = "https://some-proxy.azurewebsites.net/container/some-container/sig/somesig/se/2020-02-02/darc-int-maestro-test1-bababababab-1/index.json";
            string azdoFeed1 = "https://some_org.pkgs.visualstudio.com/_packaging/darc-int-maestro-test1-efabaababababe-1/nuget/v3/index.json";
            string azdoFeed2 = "https://some_org.pkgs.visualstudio.com/_packaging/darc-int-maestro-test1-efabaababababd-1/nuget/v3/index.json";
            string azdoFeed3 = "https://some_org.pkgs.visualstudio.com/_packaging/darc-int-maestro-test1-efabaababababf-1/nuget/v3/index.json";
            string regularFeed = "https://dotnetfeed.blob.core.windows.net/maestro-test1/index.json";
            string buildContainer = "https://dev.azure.com/dnceng/internal/_apis/build/builds/9999999/artifacts";
            string[] expectedFeeds = { proxyFeed, azdoFeed1, azdoFeed3 };
            string[] notExpectedFeeds = { regularFeed, azdoFeed2, buildContainer };

            IImmutableList<AssetData> feedFlowSourceAssets = ImmutableList.Create(
                    GetAssetDataWithLocations(
                        "Foo",
                        "1.1.0",
                        proxyFeed,
                        LocationType.NugetFeed
                        ),
                    GetAssetDataWithLocations(
                        "Bar",
                        "2.1.0",
                        azdoFeed1,
                        LocationType.NugetFeed),
                    GetAssetDataWithLocations(
                        "Pizza",
                        "3.1.0",
                        azdoFeed2,
                        LocationType.NugetFeed,
                        regularFeed,
                        LocationType.NugetFeed
                        ),
                    GetAssetDataWithLocations(
                        "Hamburger",
                        "4.1.0",
                        azdoFeed3,
                        LocationType.NugetFeed,
                        buildContainer,
                        LocationType.Container)
                    );

            TestContext.WriteLine("Azure DevOps Internal feed flow");
            TestParameters parameters = await TestParameters.GetAsync();
            SetTestParameters(parameters);

            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            List<DependencyDetail> expectedAzDoFeedFlowDependencies = new List<DependencyDetail>();

            DependencyDetail feedFoo = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = new List<string> { proxyFeed }
            };
            expectedAzDoFeedFlowDependencies.Add(feedFoo);

            DependencyDetail feedBar = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = new List<string> { azdoFeed1 }
            };
            expectedAzDoFeedFlowDependencies.Add(feedBar);

            DependencyDetail feedPizza = new DependencyDetail
            {
                Name = "Pizza",
                Version = "3.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = new List<string> { azdoFeed2, regularFeed }
            };
            expectedAzDoFeedFlowDependencies.Add(feedPizza);

            DependencyDetail feedHamburger = new DependencyDetail
            {
                Name = "Hamburger",
                Version = "4.1.0",
                RepoUri = GetAzDoRepoUrl(TestRepository.TestRepo1Name),
                Commit = TestRepository.CoherencyTestRepo1Commit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = new List<string> { azdoFeed3, buildContainer }
            };
            expectedAzDoFeedFlowDependencies.Add(feedHamburger);
            await testLogic.NonBatchedAzDoFlowTestBase(
                $"AzDo_FeedFlowBranch_{Environment.MachineName}",
                $"AzDo_FeedFlowChannel_{Environment.MachineName}",
                feedFlowSourceAssets,
                expectedAzDoFeedFlowDependencies,
                isFeedTest: true,
                expectedFeeds: expectedFeeds,
                notExpectedFeeds: notExpectedFeeds).ConfigureAwait(false);
        }
    }
}
