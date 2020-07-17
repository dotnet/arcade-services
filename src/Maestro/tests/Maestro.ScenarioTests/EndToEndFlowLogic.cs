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
        //private string sourceCommit = "123456";
        private string sourceCommit = "cc1a27107a1f4c4bc5e2f796c5ef346f60abb404";
        private string source2Commit = "789101";
        private string sourceBranch = "master";
        private readonly IImmutableList<AssetData> source1Assets;
        private readonly IImmutableList<AssetData> source2Assets;
        private readonly IImmutableList<AssetData> source1AssetsUpdated;
        IImmutableList<AssetData> childSourceBuildAssets;
        IImmutableList<AssetData> childSourceAssets;
        private List<DependencyDetail> expectedDependenciesSource1;
        private List<DependencyDetail> expectedDependenciesSource2;
        private List<DependencyDetail> expectedDependenciesSource1Updated;
        private List<DependencyDetail> expectedCoherencyDependencies;
        private TestParameters _parameters;

        public EndToEndFlowLogic(TestParameters parameters)
        {
            _parameters = parameters;
            SetTestParameters(_parameters);

            source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
            source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
            source1AssetsUpdated = GetAssetData("Foo", "1.17.0", "Bar", "2.17.0");
            childSourceBuildAssets = GetAssetData("Baz", "1.3.0", "Bop", "1.0");
            childSourceAssets = GetSingleAssetData("Baz", "1.3.0");

            expectedDependenciesSource1 = new List<DependencyDetail>();
            string sourceRepoUri = GetRepoUrl(testRepo1Name);
            DependencyDetail foo = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1.Add(foo);

            DependencyDetail bar = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1.Add(bar);

            expectedDependenciesSource2 = new List<DependencyDetail>();
            string source2RepoUri = GetRepoUrl(testRepo3Name);
            DependencyDetail pizza = new DependencyDetail
            {
                Name = "Pizza",
                Version = "3.1.0",
                RepoUri = source2RepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource2.Add(pizza);

            DependencyDetail hamburger = new DependencyDetail
            {
                Name = "Hamburger",
                Version = "4.1.0",
                RepoUri = source2RepoUri,
                Commit = sourceCommit,
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
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1Updated.Add(fooUpdated);

            DependencyDetail barUpdated = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedDependenciesSource1Updated.Add(barUpdated);

            expectedCoherencyDependencies = new List<DependencyDetail>();
            DependencyDetail baz = new DependencyDetail
            {
                Name = "Baz",
                Version = "1.3.0",
                RepoUri = GetRepoUrl(testRepo3Name),
                Commit = "8460158878d4b7568f55d27960d4453877523ea6",
                Type = DependencyType.Product,
                Pinned = false,
                CoherentParentDependencyName = "Foo"
            };

            DependencyDetail parentFoo = new DependencyDetail
            {
                Name = "Foo",
                Version = "1.1.0",
                RepoUri = sourceRepoUri,
                Commit = "cc1a27107a1f4c4bc5e2f796c5ef346f60abb404",
                Type = DependencyType.Product,
                Pinned = false
            };

            DependencyDetail parentBar = new DependencyDetail
            {
                Name = "Bar",
                Version = "2.1.0",
                RepoUri = sourceRepoUri,
                Commit = "cc1a27107a1f4c4bc5e2f796c5ef346f60abb404",
                Type = DependencyType.Product,
                Pinned = false
            };
            expectedCoherencyDependencies.Add(parentFoo);
            expectedCoherencyDependencies.Add(parentBar);
            expectedCoherencyDependencies.Add(baz);
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
                        Build build1 = await CreateBuildAsync(source1RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source1Assets);
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
                                await AddDependenciesToLocalRepo(reposFolder.Directory, source1Assets.ToList(), targetRepoUri);
                                await AddDependenciesToLocalRepo(reposFolder.Directory, source2Assets.ToList(), targetRepoUri);

                                TestContext.WriteLine("Pushing branch to remote");
                                await GitCommitAsync("Add dependencies");
                                await using (await PushGitBranchAsync("origin", targetBranch))
                                {
                                    TestContext.WriteLine("Trigger the dependency update");
                                    await TriggerSubscriptionAsync(subscription1Id.Value);
                                    await TriggerSubscriptionAsync(subscription2Id.Value);

                                    List<DependencyDetail> expectedDependencies = expectedDependenciesSource1.Concat(expectedDependenciesSource2).ToList();

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

        public async Task NonBatchedFlowTestBase(string targetBranch, string channelName, bool isAzDoTest, bool allChecks = false, bool isCoherencyTest = false)
        {
            string targetRepoName = testRepo2Name;
            string sourceRepoName = testRepo1Name;
            string childRepoName = testRepo2Name;

            if (isCoherencyTest)
            {
                targetRepoName = testRepo3Name;
                sourceBranch = "coherency-tree";
                sourceCommit = "cc1a27107a1f4c4bc5e2f796c5ef346f60abb404";
                source2Commit = "8460158878d4b7568f55d27960d4453877523ea6";
            }

            string testChannelName = channelName;
            string sourceRepoUri = isAzDoTest ? GetAzDoRepoUrl(sourceRepoName) : GetRepoUrl(sourceRepoName);
            string targetRepoUri = isAzDoTest ? GetAzDoRepoUrl(targetRepoName) : GetRepoUrl(targetRepoName);
            string childRepoUri = GetRepoUrl(childRepoName);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                await using (AsyncDisposableValue<string> subscription1Id = allChecks ? await CreateSubscriptionAsync(testChannelName, testRepo1Name, testRepo2Name, targetBranch,
                        UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: new List<string> { "--all-checks-passed", "--ignore-checks", "license/cla" }, trigger: true)
                    : await CreateSubscriptionAsync(testChannelName, sourceRepoName, targetRepoName, targetBranch,
                         UpdateFrequency.None.ToString(), "maestro-auth-test"))
                {
                    TestContext.WriteLine("Set up build for intake into target repository");
                    Build build = await CreateBuildAsync(sourceRepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source1Assets);
                    await AddBuildToChannelAsync(build.Id, testChannelName);

                    if (isCoherencyTest)
                    {
                        Build build2 = await CreateBuildAsync(GetRepoUrl(childRepoName), sourceBranch, source2Commit,
                            source2BuildNumber, childSourceBuildAssets);
                        await AddBuildToChannelAsync(build2.Id, testChannelName);
                    }

                    TestContext.WriteLine("Cloning target repo to prepare the target branch");
                    TemporaryDirectory reposFolder = await CloneRepositoryAsync(targetRepoName);
                    using (ChangeDirectory(reposFolder.Directory))
                    {
                        await using (await CheckoutBranchAsync(targetBranch))
                        {
                            TestContext.WriteLine("Adding dependencies to target repo");
                            await AddDependenciesToLocalRepo(reposFolder.Directory, source1Assets.ToList(), sourceRepoUri);

                            if (isCoherencyTest)
                            {
                                await AddDependenciesToLocalRepo(reposFolder.Directory, childSourceAssets.ToList(), targetRepoUri, "Foo");
                            }

                            TestContext.WriteLine("Pushing branch to remote");
                            await GitCommitAsync("Add dependencies");

                            await using (await PushGitBranchAsync("origin", targetBranch))
                            {
                                TestContext.WriteLine("Trigger the dependency update");
                                await TriggerSubscriptionAsync(subscription1Id.Value);

                                TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                                // AllChecks & Coherency don't take updates that it'll be merged after the first push, so no further updates will be run
                                if (allChecks)
                                {
                                    if (isAzDoTest)
                                    {
                                        await CheckNonBatchedAzDoPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependenciesSource1, reposFolder.Directory, isCompleted: true, isUpdated: false);
                                    }
                                    else
                                    {
                                        await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependenciesSource1, reposFolder.Directory, isCompleted: true, isUpdated: false);
                                    }
                                    return;
                                }

                                if (isCoherencyTest)
                                {
                                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedCoherencyDependencies, reposFolder.Directory, isCompleted: false, isUpdated: false);
                                    return;
                                }

                                // Non-Batched tests that don't use AllChecks continue to make sure updating works as expected
                                await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependenciesSource1, reposFolder.Directory, false);


                                TestContext.WriteLine("Set up another build for intake into target repository");


                                Build build2 = await CreateBuildAsync(sourceRepoUri, sourceBranch, source2Commit, source2BuildNumber, source1AssetsUpdated);

                                TestContext.WriteLine("Trigger the dependency update");
                                await TriggerSubscriptionAsync(subscription1Id.Value);

                                TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");
                                if (isAzDoTest)
                                {
                                    await CheckNonBatchedAzDoPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependenciesSource1Updated, reposFolder.Directory);
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

                                if (isAzDoTest)
                                {
                                    await CheckNonBatchedAzDoPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependenciesSource1Updated, reposFolder.Directory);
                                }
                                else
                                {
                                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependenciesSource1, reposFolder.Directory, false, true);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
