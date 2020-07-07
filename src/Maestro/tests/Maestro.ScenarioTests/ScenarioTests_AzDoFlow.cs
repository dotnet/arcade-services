using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [Category("PostDeployment")]
    class ScenarioTests_AzDoFlow : MaestroScenarioTestBase
    {
        private TestParameters _parameters;
        private EndToEndFlowLogic testLogic;

        public ScenarioTests_AzDoFlow()
        {
        }

        [SetUp]
        public async Task InitializeAsync()
        {
            _parameters = await TestParameters.GetAsync();
            SetTestParameters(_parameters);

            testLogic = new EndToEndFlowLogic(_parameters);
        }

        [TearDown]
        public Task DisposeAsync()
        {
            _parameters.Dispose();
            return Task.CompletedTask;
        }

        [Test]
        public async Task Darc_AzDoFlow_Batched()
        {
            TestContext.WriteLine("Azure DevOps Dependency Flow, batched");
            await testLogic.DarcBatchedFlowTestBase($"AzDo_BatchedTestBranch_{Environment.MachineName}", $"AzDo Batched Channel {Environment.MachineName}", true);
        }

        [Test]
        public async Task Darc_AzDoFlow_NonBatched_AllChecksSuccessful()
        {
            TestContext.WriteLine("AzDo Dependency Flow, non-batched, all checks successful");
            await testLogic.NonBatchedFlowTestBase($"AzDo_NonBatchedTestBranch_AllChecks_{Environment.MachineName}", $"AzDo Non-Batched All Checks Channel {Environment.MachineName}", true, true).ConfigureAwait(true);
        }

        [Test]
        public async Task Darc_AzDoFlow_NonBatched()
        {
            TestContext.WriteLine("AzDo Dependency Flow, non-batched");
            await testLogic.NonBatchedFlowTestBase($"AzDo_NonBatchedTestBranch_{Environment.MachineName}", $"AzDo Non-Batched Channel {Environment.MachineName}", true).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_AzDoFlow_FeedFlow()
        {
            TestContext.WriteLine("Azure DevOps Internal feed flow");

            string sourceRepoName = "maestro-test1";
            string targetRepoName = "maestro-test2";
            string sourceBranch = "master";
            string testChannelName = $"AzDo_FeedFlowChannel_{Environment.MachineName}";
            string targetBranch = $"AzDo_FeedFlowBranch_{Environment.MachineName}";
            string sourceBuildNumber = "123456";
            string sourceCommit = "654321";



            string proxyFeed = "https://some-proxy.azurewebsites.net/container/some-container/sig/somesig/se/2020-02-02/darc-int-maestro-test1-bababababab-1/index.json";
            string azdoFeed1 = "https://some_org.pkgs.visualstudio.com/_packaging/darc-int-maestro-test1-efabaababababe-1/nuget/v3/index.json";
            string azdoFeed2 = "https://some_org.pkgs.visualstudio.com/_packaging/darc-int-maestro-test1-efabaababababd-1/nuget/v3/index.json";
            string azdoFeed3 = "https://some_org.pkgs.visualstudio.com/_packaging/darc-int-maestro-test1-efabaababababf-1/nuget/v3/index.json";
            string regularFeed = "https://dotnetfeed.blob.core.windows.net/maestro-test1/index.json";
            string buildContainer = "https://dev.azure.com/dnceng/internal/_apis/build/builds/9999999/artifacts";

            IImmutableList<AssetData> source1Assets = ImmutableList.Create(
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
                    LocationType.NugetFeed)
                );

            IImmutableList<AssetData> source2Assets = ImmutableList.Create(
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
                    "2.1.0",
                    azdoFeed3,
                    LocationType.NugetFeed,
                    buildContainer,
                    LocationType.Container)
                );

            string sourceRepoUri = GetRepoUrl(sourceRepoName);
            List<DependencyDetail> expectedDependenciesSource = new List<DependencyDetail>();


            DependencyDetail foo = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = new List<string> { proxyFeed }
            };
            expectedDependenciesSource.Add(foo);

            DependencyDetail bar = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = new List<string> { azdoFeed1 }
            };
            expectedDependenciesSource.Add(bar);

            DependencyDetail pizza = new DependencyDetail
            {
                Name = "Pizza",
                Version = "3.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = new List<string> { azdoFeed2, regularFeed }
            };
            expectedDependenciesSource.Add(pizza);

            DependencyDetail hamburger = new DependencyDetail
            {
                Name = "Hamburger",
                Version = "4.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false,
                Locations = new List<string> { azdoFeed3, buildContainer }
            };
            expectedDependenciesSource.Add(hamburger);


            await new Task(() => throw new NotImplementedException());
        }
    }
}
