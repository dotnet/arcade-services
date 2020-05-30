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
    public class ScenarioTests_GitHubFlow : MaestroScenarioTestBase
    {
        private readonly string testRepo1Name = "maestro-test1";
        private readonly string testRepo2Name = "maestro-test2";
        private readonly string testRepo3Name = "maestro-test3";
        private readonly string sourceBuildNumber = "654321";
        private readonly string source2BuildNumber = "987654";
        private readonly string sourceCommit = "123456";
        private readonly string targetBranch = "GitHubFlowBranch";
        private readonly string testChannelName = "GitHub Flow Test Channel";
        private TestParameters _parameters;
        private EndToEndFlowLogic testLogic;

        public ScenarioTests_GitHubFlow()
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
        [Category("ScenarioTest")]
        public async Task Darc_GitHubFlow_Batched()
        {
            TestContext.WriteLine("Github Dependency Flow, batched");
            await testLogic.DarcBatchedFlowTestBase(false).ConfigureAwait(false);
        }

        [Test]
        [Category("ScenarioTest")]
        public async Task Darc_GitHubFlow_NonBatched_AllChecksSuccessful()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched, all checks successful");
            await testLogic.NonBatchedAllChecksTestBase(false).ConfigureAwait(false);
        }

        [Test]
        [Category("ScenarioTest")]
        public async Task Darc_GitHubFlow_NonBatched()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched");
            await testLogic.NonBatchedFlowTestBase(false).ConfigureAwait(false);
        }

        [Test]
        [Category("ScenarioTest")]
        public async Task Darc_GitHubFlow_NonBatched_WithCoherency()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched with required coherency updates");

            //ParentSource = testRepo1, ChildSource = testRepo2, TargetRepo = testRepo3
            string parentSourceRepoUri = GetRepoUrl(testRepo1Name);
            string childSourceRepoUri = GetRepoUrl(testRepo2Name);

            // source commit is set to the HEAD commit of the "coherency-tree" branch
            string sourceBranch = "coherency-tree";
            string parentSourceCommit = "cc1a27107a1f4c4bc5e2f796c5ef346f60abb404";
            string childSourceCommit = "8460158878d4b7568f55d27960d4453877523ea6";
            IImmutableList<AssetData> childSourceAssets = GetAssetData("Baz", "1.3.0", "Bop", "1.0");

            List<DependencyDetail> expectedChildDependencies = new List<DependencyDetail>();
            DependencyDetail dep1 = new DependencyDetail
            {
                Name = "Baz",
                Version = "1.3.0",
                RepoUri = childSourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedChildDependencies.Add(dep1);

            DependencyDetail dep2 = new DependencyDetail
            {
                Name = "Bop",
                Version = "1.0",
                RepoUri = childSourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedChildDependencies.Add(dep2);

            TestContext.WriteLine($"Creating a test channel{testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                TestContext.WriteLine($"Adding a subscription from {testRepo1Name} to {testRepo3Name}");

                await using (AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(
                    testChannelName, testRepo1Name, testRepo3Name, targetBranch, UpdateFrequency.None.ToString(), "maestro-auth-test"))
                {
                    TestContext.WriteLine("Set up new builds for intake into target repository");
                    TemporaryDirectory tempDirectory = await testLogic.SetUpForTwoSourceSub(
                        testChannelName,
                        parentSourceRepoUri, sourceBranch, parentSourceCommit, sourceBuildNumber, testLogic.Source1Assets,
                        childSourceRepoUri, sourceBranch, childSourceCommit, source2BuildNumber, childSourceAssets,
                        testRepo3Name, targetBranch, "Foo");

                    using (ChangeDirectory(tempDirectory.Directory))
                    {
                        await using (await PushGitBranchAsync("origin", targetBranch))
                        {
                            await TriggerSubscriptionAsync(subscription1Id.Value);

                            List<DependencyDetail> expectedDependencies = testLogic.ExpectedDependenciesSource1.Concat(expectedChildDependencies).ToList();

                            await CheckNonBatchedGitHubPullRequest(testRepo1Name, testRepo3Name, targetBranch, expectedDependencies, tempDirectory.Directory);
                        }
                    }
                }
            }
        }
    }
}
