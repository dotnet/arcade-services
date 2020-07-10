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
    public class EndToEndFlowLogic : MaestroScenarioTestBase
    {
        private readonly string testRepo1Name = "maestro-test1";
        private readonly string testRepo2Name = "maestro-test2";
        private readonly string testRepo3Name = "maestro-test3";
        private readonly string sourceBuildNumber = "654321";
        private readonly string source2BuildNumber = "987654";
        private readonly string sourceCommit = "123456";
        private readonly string source2Commit = "789101";
        private readonly string sourceBranch = "master";
        private readonly string gitHubBranchName = "GitHubTestFlowBranch";
        private readonly string azDoBranchName = "AzDoTestFlowBranch";
        private readonly string gitHubChannelName = "GitHub Flow Test Channel";
        private readonly string azDoChannelName = "AzDo Flow Test Channel";
        internal readonly IImmutableList<AssetData> Source1Assets;
        private readonly IImmutableList<AssetData> source2Assets;
        private readonly IImmutableList<AssetData> source3Assets;
        internal List<DependencyDetail> ExpectedDependenciesSource1;
        private List<DependencyDetail> expectedDependenciesSource2;
        private List<DependencyDetail> expectedDependenciesSource3;
        private TestParameters _parameters;

        public EndToEndFlowLogic(TestParameters parameters)
        {
            _parameters = parameters;
            SetTestParameters(_parameters);

            Source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
            source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
            source3Assets = GetAssetData("Foo", "1.17.0", "Bar", "2.17.0");

            ExpectedDependenciesSource1 = new List<DependencyDetail>();
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
            ExpectedDependenciesSource1.Add(dep1);

            DependencyDetail dep2 = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            ExpectedDependenciesSource1.Add(dep2);

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

        public async Task DarcBatchedFlowTestBase(bool isAzDoTest)
        {
            string testChannelName = isAzDoTest ? azDoChannelName : gitHubChannelName;
            string targetBranch = isAzDoTest ? azDoBranchName : gitHubBranchName;

            string source1RepoUri = isAzDoTest ? GetAzDoRepoUrl(testRepo1Name) : GetRepoUrl(testRepo1Name);
            string source2RepoUri = isAzDoTest ? GetAzDoRepoUrl(testRepo3Name) : GetRepoUrl(testRepo3Name);
            string targetRepoUri = isAzDoTest ? GetAzDoRepoUrl(testRepo2Name) : GetRepoUrl(testRepo2Name);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                TestContext.WriteLine($"Adding a subscription from {testRepo1Name} to {testRepo2Name}");
                await using (AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(testChannelName, testRepo1Name, testRepo2Name, targetBranch,
                    UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: new List<string> { "--batchable" }))
                {
                    TestContext.WriteLine($"Adding a subscription from {testRepo3Name} to {testRepo2Name}");
                    await using (AsyncDisposableValue<string> subscription2Id = await CreateSubscriptionAsync(testChannelName, testRepo3Name, testRepo2Name, targetBranch,
                        UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: new List<string> { "--batchable" }))
                    {

                        TestContext.WriteLine("Set up build1 for intake into target repository");
                        Build build1 = await CreateBuildAsync(source1RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, Source1Assets);
                        await AddBuildToChannelAsync(build1.Id, testChannelName);

                        TestContext.WriteLine("Set up build2 for intake into target repository");
                        Build build2 = await CreateBuildAsync(source2RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source2Assets);
                        await AddBuildToChannelAsync(build2.Id, testChannelName);


                        TestContext.WriteLine("Cloning target repo to prepare the target branch");
                        TemporaryDirectory reposFolder = await CloneRepositoryAsync(testRepo2Name);
                        using (ChangeDirectory(reposFolder.Directory))
                        {
                            await using (await CheckoutBranchAsync(targetBranch))
                            {
                                TestContext.WriteLine("Adding dependencies to target repo");
                                await AddDependenciesToLocalRepo(reposFolder.Directory, Source1Assets.ToList(), targetRepoUri);
                                await AddDependenciesToLocalRepo(reposFolder.Directory, source2Assets.ToList(), targetRepoUri);

                                TestContext.WriteLine("Pushing branch to remote");
                                await GitCommitAsync("Add dependencies");
                                await using (await PushGitBranchAsync("origin", targetBranch))
                                {
                                    TestContext.WriteLine("Trigger the dependency update");
                                    await TriggerSubscriptionAsync(subscription1Id.Value);
                                    await TriggerSubscriptionAsync(subscription2Id.Value);

                                    List<DependencyDetail> expectedDependencies = ExpectedDependenciesSource1.Concat(expectedDependenciesSource2).ToList();

                                    TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                                    if (isAzDoTest)
                                    {
                                        await CheckBatchedAzDoPullRequest(testRepo1Name, testRepo3Name, testRepo2Name, targetBranch, expectedDependencies, reposFolder.Directory);
                                    }
                                    else
                                    {
                                        await CheckBatchedGitHubPullRequest(targetBranch, testRepo1Name, testRepo2Name, expectedDependencies, reposFolder.Directory);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public async Task NonBatchedFlowTestBase(bool isAzDoTest, bool allChecks = false)
        {
            string targetRepoName = testRepo2Name;
            string sourceRepoName = testRepo1Name;

            string testChannelName = isAzDoTest ? azDoChannelName : gitHubChannelName;
            string targetBranch = isAzDoTest ? azDoBranchName : gitHubBranchName;

            string sourceRepoUri = isAzDoTest ? GetAzDoRepoUrl(sourceRepoName) : GetRepoUrl(sourceRepoName);
            string targetRepoUri = isAzDoTest ? GetAzDoRepoUrl(targetRepoName) : GetRepoUrl(targetRepoName);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                await using (AsyncDisposableValue<string> subscription1Id = allChecks ? await CreateSubscriptionAsync(testChannelName, testRepo1Name, testRepo2Name, targetBranch,
                        UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: new List<string> { "--all-checks-passed", "--ignore-checks", "license/cla" }, trigger: true) 
                    : await CreateSubscriptionAsync(testChannelName, sourceRepoName, targetRepoName, targetBranch,
                         UpdateFrequency.None.ToString(), "maestro-auth-test"))
                {
                    TestContext.WriteLine("Set up build for intake into target repository");
                    Build build = await CreateBuildAsync(sourceRepoUri, sourceBranch, sourceCommit, sourceBuildNumber, Source1Assets);
                    await AddBuildToChannelAsync(build.Id, testChannelName);

                    TestContext.WriteLine("Cloning target repo to prepare the target branch");
                    TemporaryDirectory reposFolder = await CloneRepositoryAsync(targetRepoName);
                    using (ChangeDirectory(reposFolder.Directory))
                    {
                        await using (await CheckoutBranchAsync(targetBranch))
                        {

                            TestContext.WriteLine("Adding dependencies to target repo");
                            await AddDependenciesToLocalRepo(reposFolder.Directory, Source1Assets.ToList(), sourceRepoUri);

                            TestContext.WriteLine("Pushing branch to remote");
                            await GitCommitAsync("Add dependencies");

                            await using (await PushGitBranchAsync("origin", targetBranch))
                            {
                                TestContext.WriteLine("Trigger the dependency update");
                                await TriggerSubscriptionAsync(subscription1Id.Value);

                                TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                                await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, ExpectedDependenciesSource1, reposFolder.Directory, true);
                            }

                            TestContext.WriteLine("Set up another build for intake into target repository");
                            Build build2 = await CreateBuildAsync(sourceRepoUri, sourceBranch, source2Commit, source2BuildNumber, source3Assets);

                            TestContext.WriteLine("Trigger the dependency update");
                            await TriggerSubscriptionAsync(subscription1Id.Value);

                            TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");
                            await ValidatePullRequestDependencies(targetRepoName, targetBranch, expectedDependenciesSource3);

                            // Then remove the second build from the channel, trigger the sub again, and it should revert back to the original dependency set
                            TestContext.Write("Remove the build from the channel and verify that the original dependencies are restored");
                            await DeleteBuildFromChannelAsync(build2.Id, testChannelName);

                            TestContext.WriteLine("Trigger the dependency update");
                            await TriggerSubscriptionAsync(subscription1Id.Value);

                            TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");
                            await ValidatePullRequestDependencies(targetRepoName, targetBranch, ExpectedDependenciesSource1);
                        }
                    }
                }
            }
        }

        public async Task NonBatchedAllChecksTestBase(bool isAzDoTest)
        {
            string testChannelName = isAzDoTest ? azDoChannelName : gitHubChannelName;
            string targetBranch = isAzDoTest ? azDoBranchName : gitHubBranchName;

            string sourceRepoName = testRepo1Name;
            string targetRepoName = testRepo2Name;

            string sourceRepoUri = isAzDoTest ? GetAzDoRepoUrl(testRepo1Name) : GetRepoUrl(testRepo1Name);
            string targetRepoUri = isAzDoTest ? GetAzDoRepoUrl(testRepo2Name) : GetRepoUrl(testRepo2Name);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                TemporaryDirectory reposFolder = await SetUpForSingleSourceSub(
                    testChannelName, sourceRepoUri, sourceBranch, sourceCommit, sourceBuildNumber, Source1Assets,
                    testRepo2Name, targetBranch);
                using (ChangeDirectory(reposFolder.Directory))
                {
                    await using (await PushGitBranchAsync("origin", targetBranch))
                    {
                        await using (AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(testChannelName, testRepo1Name, testRepo2Name, targetBranch,
                             UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: new List<string> { "--all-checks-passed", "--ignore-checks", "license/cla" }, trigger: true))
                        {
                            TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                            await CheckNonBatchedGitHubPullRequest(testRepo1Name, testRepo2Name, targetBranch, ExpectedDependenciesSource1, reposFolder.Directory, true);
                        }
                    }
                }
            }
        }

        public async Task<TemporaryDirectory> SetUpForSingleSourceSub(
            string testChannelName,
            string sourceRepoUri,
            string sourceBranch,
            string sourceCommit,
            string sourceBuildNumber,
            IImmutableList<AssetData> sourceAssets,
            string targetRepoName,
            string targetBranch)
        {
            TestContext.WriteLine("Set up build for intake into target repository");
            Build build = await CreateBuildAsync(sourceRepoUri, sourceBranch, sourceCommit, sourceBuildNumber, sourceAssets);
            await AddBuildToChannelAsync(build.Id, testChannelName);

            TestContext.WriteLine("Cloning target repo to prepare the target branch");
            TemporaryDirectory reposFolder = await CloneRepositoryAsync(targetRepoName);
            using (ChangeDirectory(reposFolder.Directory))
            {
                await CheckoutBranchAsync(targetBranch);

                TestContext.WriteLine("Adding dependencies to target repo");
                await AddDependenciesToLocalRepo(reposFolder.Directory, sourceAssets.ToList(), sourceRepoUri);

                TestContext.WriteLine("Pushing branch to remote");
                await GitCommitAsync("Add dependencies");
            }

            return reposFolder;
        }
    }
}
