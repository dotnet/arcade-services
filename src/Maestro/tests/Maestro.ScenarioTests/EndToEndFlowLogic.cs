using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Maestro.ScenarioTests
{
    public class EndToEndFlowLogic : MaestroScenarioTestBase
    {
        private readonly string sourceBuildNumber = "654321";
        private readonly string source2BuildNumber = "987654";
        private readonly IImmutableList<AssetData> source1Assets;
        private readonly IImmutableList<AssetData> source2Assets;
        private readonly IImmutableList<AssetData> source1AssetsUpdated;
        private readonly IImmutableList<AssetData> childSourceBuildAssets;
        private readonly IImmutableList<AssetData> childSourceAssets;
        private readonly List<DependencyDetail> expectedDependenciesSource1;
        private readonly List<DependencyDetail> expectedDependenciesSource2;
        private readonly List<DependencyDetail> expectedDependenciesSource1Updated;
        private readonly List<DependencyDetail> expectedCoherencyDependencies;
        private readonly TestParameters _parameters;

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

        public async Task DarcBatchedFlowTestBase(string targetBranch, string channelName, bool isAzDoTest)
        {
            string source1RepoName = TestRepository.TestRepo1Name;
            string source2RepoName = TestRepository.TestRepo3Name;
            string targetRepoName = TestRepository.TestRepo2Name;

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
                        Build build1 = await CreateBuildAsync(source1RepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, sourceBuildNumber, source1Assets);
                        await AddBuildToChannelAsync(build1.Id, testChannelName);

                        TestContext.WriteLine("Set up build2 for intake into target repository");
                        Build build2 = await CreateBuildAsync(source2RepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, sourceBuildNumber, source2Assets);
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
                                        throw new NotImplementedException("AzDo Flow Tests are not part of the scope for this change.");
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
            string targetRepoName = TestRepository.TestRepo2Name;
            string sourceRepoName = TestRepository.TestRepo1Name;
            string childRepoName = TestRepository.TestRepo1Name;
            string sourceBranch = TestRepository.SourceBranch;

            if (isCoherencyTest)
            {
                sourceRepoName = TestRepository.TestRepo2Name;
                targetRepoName = TestRepository.TestRepo3Name;
                sourceBranch = TestRepository.CoherencySourceBranch;
            }

            string testChannelName = channelName;
            string sourceRepoUri = isAzDoTest ? GetAzDoRepoUrl(sourceRepoName) : GetRepoUrl(sourceRepoName);
            string targetRepoUri = isAzDoTest ? GetAzDoRepoUrl(targetRepoName) : GetRepoUrl(targetRepoName);
            string childSourceRepoUri = isAzDoTest ? GetAzDoRepoUrl(childRepoName) : GetRepoUrl(childRepoName);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using (AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionForEndToEndTests(
                    testChannelName, sourceRepoName, targetRepoName, targetBranch, allChecks);

                TestContext.WriteLine("Set up build for intake into target repository");
                Build build = await CreateBuildAsync(sourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, sourceBuildNumber, source1Assets);
                await AddBuildToChannelAsync(build.Id, testChannelName);

                if (isCoherencyTest)
                {
                    Build build2 = await CreateBuildAsync(childSourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo2Commit,
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
                            await AddDependenciesToLocalRepo(reposFolder.Directory, childSourceAssets.ToList(), childSourceRepoUri, "Foo");
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
                                    throw new NotImplementedException("AzDo Flow Tests are not part of the scope for this change.");
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


                            Build build2 = await CreateBuildAsync(sourceRepoUri, sourceBranch, TestRepository.CoherencyTestRepo2Commit, source2BuildNumber, source1AssetsUpdated);

                            TestContext.WriteLine("Trigger the dependency update");
                            await TriggerSubscriptionAsync(subscription1Id.Value);

                            TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");
                            if (isAzDoTest)
                            {
                                throw new NotImplementedException("AzDo Flow Tests are not part of the scope for this change.");
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
                                throw new NotImplementedException("AzDo Flow Tests are not part of the scope for this change.");
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

        private async Task<AsyncDisposableValue<string>> CreateSubscriptionForEndToEndTests(string testChannelName, string sourceRepoName, 
            string targetRepoName, string targetBranch, bool allChecks)
        {
            if (allChecks)
            {

                return await CreateSubscriptionAsync(
                    testChannelName,
                    sourceRepoName,
                    targetRepoName,
                    targetBranch,
                    UpdateFrequency.None.ToString(),
                    "maestro-auth-test",
                    additionalOptions: new List<string> { "--all-checks-passed", "--ignore-checks", "license/cla" },
                    trigger: true);
            }
            else
            {
                return await CreateSubscriptionAsync(
                testChannelName,
                sourceRepoName,
                targetRepoName,
                targetBranch,
                 UpdateFrequency.None.ToString(),
                 "maestro-auth-test");
            }
        }
    }
}
