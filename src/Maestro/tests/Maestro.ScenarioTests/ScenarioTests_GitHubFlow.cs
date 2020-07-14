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
    [Category("PostDeployment")]
    public class ScenarioTests_GitHubFlow : MaestroScenarioTestBase
    {
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
        public async Task Darc_GitHubFlow_Batched()
        {
            TestContext.WriteLine("Github Dependency Flow, batched");
            await testLogic.DarcBatchedFlowTestBase($"GitHub_BatchedTestBranch_{Environment.MachineName}", $"GitHub Batched Channel {Environment.MachineName}", false).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_AllChecksSuccessful()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched, all checks successful");
            await testLogic.NonBatchedFlowTestBase($"GitHub_NonBatchedTestBranch_AllChecks_{Environment.MachineName}", $"GitHub Non-Batched All Checks Channel {Environment.MachineName}", false, true).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched");
            await testLogic.NonBatchedFlowTestBase($"GitHub_NonBatchedTestBranch_{Environment.MachineName}", $"GitHub Non-Batched Channel {Environment.MachineName}", false).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_WithCoherency()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched with required coherency updates");

            string parentSourceRepoName = "maestro-test1";
            string childSourceRepoName = "maestro-test2";
            string targetRepoName = "maestro-test3";

            string parentSourceRepoUri = GetRepoUrl(parentSourceRepoName);
            string childSourceRepoUri = GetRepoUrl(childSourceRepoName);

            // source commit is set to the HEAD commit of the "coherency-tree" branch
            string sourceBranch = "coherency-tree";
            string parentSourceCommit = "cc1a27107a1f4c4bc5e2f796c5ef346f60abb404";
            string childSourceCommit = "8460158878d4b7568f55d27960d4453877523ea6";
            IImmutableList<AssetData> childSourceAssets = GetAssetData("Baz", "1.3.0","Bop", "1.0");
            string parentBuildNumber = "123456";
            string childBuildNumber = "654321";

            string targetBranch = $"GitHub_NonBatchedTestBranch_Coherency_{Environment.MachineName}";
            string testChannelName = $"GitHub Non-Batched Coherency Channel {Environment.MachineName}";

            List<DependencyDetail> expectedChildDependencies = new List<DependencyDetail>();
            DependencyDetail dep1 = new DependencyDetail
            {
                Name = "Baz",
                Version = "1.3.0",
                RepoUri = childSourceRepoUri,
                Commit = childSourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedChildDependencies.Add(dep1);

            TestContext.WriteLine($"Creating a test channel{testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                TestContext.WriteLine($"Adding a subscription from {parentSourceRepoName} to {targetRepoName}");

                await using (AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(
                    testChannelName, parentSourceRepoName, targetRepoName, sourceBranch, UpdateFrequency.None.ToString(), "maestro-auth-test"))
                {
                    TestContext.WriteLine("Set up new builds for intake into target repository");
                    Build build1 = await CreateBuildAsync(parentSourceRepoUri, sourceBranch, parentSourceCommit,
                        parentBuildNumber, testLogic.Source1Assets);
                    await AddBuildToChannelAsync(build1.Id, testChannelName);
                    Build build2 = await CreateBuildAsync(childSourceRepoUri, sourceBranch, childSourceCommit,
                        childBuildNumber, childSourceAssets);
                    await AddBuildToChannelAsync(build2.Id, testChannelName);

                    TestContext.WriteLine("Cloning target repo to prepare the target branch");
                    TemporaryDirectory reposFolder = await CloneRepositoryAsync(targetRepoName);

                    using (ChangeDirectory(reposFolder.Directory))
                    {
                        await using (await CheckoutBranchAsync(targetBranch))
                        {
                            TestContext.WriteLine("Adding dependencies to target repo");
                            await AddDependenciesToLocalRepo(reposFolder.Directory, testLogic.Source1Assets.ToList(), parentSourceRepoUri);
                            await AddDependenciesToLocalRepo(reposFolder.Directory, childSourceAssets.ToList(), childSourceRepoUri, "Foo");

                            TestContext.WriteLine("Pushing branch to remote");
                            await GitCommitAsync("Add dependencies");

                            await using (await PushGitBranchAsync("origin", targetBranch))
                            {
                                TestContext.WriteLine("Trigger the dependency update");
                                await TriggerSubscriptionAsync(subscription1Id.Value);

                                List<DependencyDetail> expectedDependencies = testLogic.ExpectedDependenciesSource1.Concat(expectedChildDependencies).ToList();

                                TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoName}");
                                await CheckNonBatchedGitHubPullRequest(parentSourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory);
                            }
                        }
                    }
                }
            }
        }
    }
}
