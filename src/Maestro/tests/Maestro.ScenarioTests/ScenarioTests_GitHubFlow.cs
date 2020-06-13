using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault.Models;
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
        private readonly string source2Commit = "789101";
        private readonly string sourceBranch = "master";
        private readonly string targetBranch = "GitHubFlowBranch";
        private readonly string testChannelName = "GitHub Flow Test Channel";
        private readonly IImmutableList<AssetData> source1Assets;
        private readonly IImmutableList<AssetData> source2Assets;
        private readonly IImmutableList<AssetData> source3Assets;
        private readonly List<DependencyDetail> expectedDependenciesSource1;
        private readonly List<DependencyDetail> expectedDependenciesSource2;
        private readonly List<DependencyDetail> expectedDependenciesSource3;
        private TestParameters _parameters;

        public ScenarioTests_GitHubFlow()
        {
            source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
            source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
            source3Assets = GetAssetData("Foo", "1.17.0", "Bar", "2.17.0");

            expectedDependenciesSource1 = new List<DependencyDetail>();
            string sourceRepoUri = GetRepoUrl(testRepo1Name);
            DependencyDetail dep1 = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1.Add(dep1);

            DependencyDetail dep2 = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1.Add(dep2);

            expectedDependenciesSource2 = new List<DependencyDetail>();
            string source2RepoUri = GetRepoUrl(testRepo3Name);
            DependencyDetail dep3 = new DependencyDetail
            {
                Name = "Pizza",
                Version = "3.1.0",
                RepoUri = source2RepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource2.Add(dep3);

            DependencyDetail dep4 = new DependencyDetail
            {
                Name = "Hamburger",
                Version = "4.1.0",
                RepoUri = source2RepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource2.Add(dep4);

            expectedDependenciesSource3 = new List<DependencyDetail>();
            DependencyDetail dep5 = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource3.Add(dep5);

            DependencyDetail dep6 = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource3.Add(dep6);

        }

        [SetUp]
        public async Task InitializeAsync()
        {
            _parameters = await TestParameters.GetAsync();
            SetTestParameters(_parameters);
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

            string source1RepoUri = GetRepoUrl(testRepo1Name);
            string source2RepoUri = GetRepoUrl(testRepo3Name);
            string targetRepoUri = GetRepoUrl(testRepo2Name);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {

                TestContext.WriteLine($"Adding a subscription from {testRepo1Name} to {testRepo2Name}");
                await using (AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(testChannelName, testRepo1Name, testRepo2Name, targetBranch,
                    UpdateFrequency.None.ToString(), additionalOptions: new List<string> { "--batchable" }))
                {
                    TestContext.WriteLine($"Adding a  subscription from {testRepo3Name} to {testRepo2Name}");
                    await using (AsyncDisposableValue<string> subscription2Id = await CreateSubscriptionAsync(testChannelName, testRepo3Name, testRepo2Name, targetBranch,
                        UpdateFrequency.None.ToString(), additionalOptions: new List<string> { "--batchable" }))
                    {

                        TestContext.WriteLine("Set up build1 for intake into target repository");
                        Build build1 = await CreateBuildAsync(source1RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source1Assets);
                        await AddBuildToChannelAsync(build1.Id, testChannelName);

                        TestContext.WriteLine("Set up build2 for intake into target repository");
                        Build build2 = await CreateBuildAsync(source2RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source2Assets);
                        await AddBuildToChannelAsync(build2.Id, testChannelName);

                        TestContext.WriteLine("Cloning target repo to prepare the target branch");
                        TemporaryDirectory reposFolder = await CloneRepositoryAsync(testRepo2Name);
                        await CheckoutBranchAsync(targetBranch);

                        TestContext.WriteLine("Adding dependencies to target repo");
                        await AddDependenciesToLocalRepo(reposFolder.Directory, source1Assets.ToList(), source1RepoUri);
                        await AddDependenciesToLocalRepo(reposFolder.Directory, source2Assets.ToList(), source2RepoUri);

                        TestContext.WriteLine("Pushing branch to remote");
                        await GitCommitAsync("Add dependencies");
                        await using (await PushGitBranchAsync("origin", "GitHubFlowBranch"))
                        {

                            TestContext.WriteLine("Trigger the dependency update");
                            await TriggerSubscriptionAsync(subscription1Id.Value);
                            await TriggerSubscriptionAsync(subscription2Id.Value);

                            List<DependencyDetail> expectedDependencies = expectedDependenciesSource1.Concat(expectedDependenciesSource2).ToList();

                            TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                            await CheckBatchedGitHubPullRequest(targetBranch, testRepo1Name, testRepo3Name, testRepo2Name, expectedDependencies, reposFolder.Directory);
                        }
                    }
                }
            }
        }

        [Test]
        [Category("ScenarioTest")]
        public async Task Darc_GitHubFlow_NonBatched_AllChecksSuccessful()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched, all checks successful");
            string sourceRepoUri = GetRepoUrl(testRepo1Name);
            string targetRepoUri = GetRepoUrl(testRepo2Name);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                TemporaryDirectory reposFolder = await SetUpForSingleSourceSub(sourceRepoUri);

                await using (await PushGitBranchAsync("origin", "GitHubFlowBranch"))
                {
                    await using (AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(testChannelName, testRepo1Name, testRepo2Name, targetBranch,
                         UpdateFrequency.None.ToString(), additionalOptions: new List<string> { "--all-checks-passed", "--ignore-checks license/cla", "--trigger" }))
                    {
                        TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                        await CheckNonBatchedGitHubPullRequest(targetBranch, testRepo1Name, testRepo3Name, testRepo2Name, expectedDependenciesSource1, reposFolder.Directory, true);
                    }
                }
            }
        }

        [Test]
        [Category("ScenarioTest")]
        public async Task Darc_GitHubFlow_NonBatched()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched");
            string sourceRepoUri = GetRepoUrl(testRepo1Name);
            string targetRepoUri = GetRepoUrl(testRepo2Name);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                await using (AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(testChannelName, testRepo1Name, testRepo2Name, targetBranch,
                         UpdateFrequency.None.ToString()))
                {
                    TemporaryDirectory reposFolder = await SetUpForSingleSourceSub(sourceRepoUri);

                    await using (await PushGitBranchAsync("origin", "GitHubFlowBranch"))
                    {

                        TestContext.WriteLine("Trigger the dependency update");
                        await TriggerSubscriptionAsync(subscription1Id.Value);

                        TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                        await CheckNonBatchedGitHubPullRequest(targetBranch, testRepo1Name, testRepo3Name, testRepo2Name, expectedDependenciesSource1, reposFolder.Directory, true);
                    }

                    TestContext.WriteLine("Set up another build for intake into target repository");
                    Build build = await CreateBuildAsync(sourceRepoUri, sourceBranch, source2Commit, source2BuildNumber, source3Assets);

                    TestContext.WriteLine("Trigger the dependency update");
                    await TriggerSubscriptionAsync(subscription1Id.Value);

                    TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");
                    await ValidatePullRequestDependencies(testRepo2Name, targetBranch, expectedDependenciesSource3);

                    // Then remove the second build from the channel, trigger the sub again, and it should revert back to the original dependency set
                    TestContext.Write("Remove the build from the channel and verify that the original dependencies are restored");
                    await DeleteBuildFromChannelAsync(build.Id, testChannelName);

                    TestContext.WriteLine("Trigger the dependency update");
                    await TriggerSubscriptionAsync(subscription1Id.Value);

                    TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");
                    await ValidatePullRequestDependencies(testRepo2Name, targetBranch, expectedDependenciesSource1);
                }
            }
        }

        [Test]
        [Category("ScenarioTest")]
        public async Task Darc_GitHubFlow_NonBatched_WithCoherency()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched with required coherency updates");

            //ParentSource = testRepo1, ChildSource = testRepo2, TargetRepo = testRepo3
            string parentSourceRepoUri = GetRepoUrl(testRepo1Name);
            string childSourceRepoUri = GetRepoUrl(testRepo2Name);
            string targetRepoUri = GetRepoUrl(testRepo3Name);

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

        }

            private async Task<TemporaryDirectory> SetUpForSingleSourceSub(string sourceRepoUri)
        {
            TestContext.WriteLine("Set up build for intake into target repository");
            Build build = await CreateBuildAsync(sourceRepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source1Assets);
            await AddBuildToChannelAsync(build.Id, testChannelName);

            TestContext.WriteLine("Cloning target repo to prepare the target branch");
            TemporaryDirectory reposFolder = await CloneRepositoryAsync(testRepo2Name);
            await CheckoutBranchAsync(targetBranch);

            TestContext.WriteLine("Adding dependencies to target repo");
            await AddDependenciesToLocalRepo(reposFolder.Directory, source1Assets.ToList(), sourceRepoUri);

            TestContext.WriteLine("Pushing branch to remote");
            await GitCommitAsync("Add dependencies");
            return reposFolder;
        }
    }
}
