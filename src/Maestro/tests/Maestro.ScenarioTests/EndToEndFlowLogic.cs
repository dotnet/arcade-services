using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Primitives;
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
        internal readonly IImmutableList<AssetData> Source1Assets;
        private readonly IImmutableList<AssetData> source2Assets;
        private readonly IImmutableList<AssetData> source1AssetsUpdated;
        internal List<DependencyDetail> ExpectedDependenciesSource1;
        private List<DependencyDetail> expectedDependenciesSource2;
        private List<DependencyDetail> expectedDependenciesSource1Updated;
        private TestParameters _parameters;

        public EndToEndFlowLogic(TestParameters parameters)
        {
            _parameters = parameters;
            SetTestParameters(_parameters);

            Source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
            source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
            source1AssetsUpdated = GetAssetData("Foo", "1.17.0", "Bar", "2.17.0");

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

            expectedDependenciesSource1Updated = new List<DependencyDetail>();
            DependencyDetail dep5 = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1Updated.Add(dep5);

            DependencyDetail dep6 = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1Updated.Add(dep6);
        }

        public async Task DarcBatchedFlowTestBase(string targetBranch, string channelName, bool isAzDoTest)
        {
            string source1RepoName = testRepo1Name;
            string source2RepoName = testRepo3Name;
            string targetRepoName = testRepo2Name;

            string testChannelName = channelName;
            string source1RepoUri = isAzDoTest ? GetAzDoRepoUrl(source1RepoName) : GetRepoUrl(source1RepoName);
            string source2RepoUri = isAzDoTest ? GetAzDoRepoUrl(source2RepoName) : GetRepoUrl(source2RepoName);
            string targetRepoUri = isAzDoTest ? GetAzDoRepoUrl(targetRepoName) : GetRepoUrl(targetRepoName);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                TestContext.WriteLine($"Adding a subscription from {source1RepoName} to {targetRepoName}");
                await using (AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(testChannelName, source1RepoName, targetRepoName, targetBranch,
                    UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: new List<string> { "--batchable" }))
                {
                    TestContext.WriteLine($"Adding a subscription from {source2RepoName} to {targetRepoName}");
                    await using (AsyncDisposableValue<string> subscription2Id = await CreateSubscriptionAsync(testChannelName, source2RepoName, targetRepoName, targetBranch,
                        UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: new List<string> { "--batchable" }))
                    {

                        TestContext.WriteLine("Set up build1 for intake into target repository");
                        Build build1 = await CreateBuildAsync(source1RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, Source1Assets);
                        await AddBuildToChannelAsync(build1.Id, testChannelName);

                        TestContext.WriteLine("Set up build2 for intake into target repository");
                        Build build2 = await CreateBuildAsync(source2RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source2Assets);
                        await AddBuildToChannelAsync(build2.Id, testChannelName);


                        TestContext.WriteLine("Cloning target repo to prepare the target branch");
                        TemporaryDirectory reposFolder = await CloneRepositoryAsync(targetRepoName);
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
                                        await CheckBatchedAzDoPullRequest(source1RepoName, source2RepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory);
                                    }
                                    else
                                    {
                                        await CheckBatchedGitHubPullRequest(targetBranch, source1RepoName, source2RepoName, targetRepoName, expectedDependencies, reposFolder.Directory);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public async Task NonBatchedFlowTestBase(string targetBranch, string channelName, bool isAzDoTest, bool allChecks = false)
        {
            string targetRepoName = testRepo2Name;
            string sourceRepoName = testRepo1Name;

            string testChannelName = channelName;
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

                                // AllChecks means that it'll be merged after the first push, so no further updates will be run
                                if (allChecks)
                                {
                                    if (isAzDoTest)
                                    {
                                        await CheckNonBatchedAzDoPullRequest(sourceRepoName, targetRepoName, targetBranch, ExpectedDependenciesSource1, reposFolder.Directory);
                                    }
                                    else
                                    {
                                        await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, ExpectedDependenciesSource1, reposFolder.Directory, false, true);
                                    }

                                    return;
                                }

                                // Non-Batched tests that don't use AllChecks continue to make sure updating works as expected
                                await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, ExpectedDependenciesSource1, reposFolder.Directory, false);


                                TestContext.WriteLine("Set up another build for intake into target repository");
                                Build build2 = await CreateBuildAsync(sourceRepoUri, sourceBranch, source2Commit, source2BuildNumber, source1AssetsUpdated);

                                TestContext.WriteLine("Trigger the dependency update");
                                await TriggerSubscriptionAsync(subscription1Id.Value);

                                TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");

                                if (allChecks)
                                {
                                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependenciesSource1Updated, reposFolder.Directory, true, true);
                                }
                                else
                                {
                                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependenciesSource1Updated, reposFolder.Directory, false, true);
                                }

                                // Then remove the second build from the channel, trigger the sub again, and it should revert back to the original dependency set
                                TestContext.Write("Remove the build from the channel and verify that the original dependencies are restored");
                                await DeleteBuildFromChannelAsync(build2.Id.ToString(), testChannelName);

                                TestContext.WriteLine("Trigger the dependency update");
                                await TriggerSubscriptionAsync(subscription1Id.Value);

                                TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");

                                if (allChecks)
                                {
                                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, ExpectedDependenciesSource1, reposFolder.Directory, true, true);
                                }
                                else
                                {
                                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, ExpectedDependenciesSource1, reposFolder.Directory, false, true);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
