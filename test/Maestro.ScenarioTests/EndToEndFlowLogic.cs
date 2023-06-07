using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;

namespace Maestro.ScenarioTests
{
    public class EndToEndFlowLogic : MaestroScenarioTestBase
    {
        private readonly string sourceBuildNumber = "654321";
        private readonly string source2BuildNumber = "987654";
        private readonly TestParameters _parameters;

        public EndToEndFlowLogic(TestParameters parameters)
        {
            _parameters = parameters;
            SetTestParameters(_parameters);
        }

        public async Task DarcBatchedFlowTestBase(string targetBranch, string channelName, IImmutableList<AssetData> source1Assets, IImmutableList<AssetData> source2Assets,
            List<DependencyDetail> expectedDependencies, bool isAzDoTest)
        {
            string source1RepoName = TestRepository.TestRepo1Name;
            string source2RepoName = TestRepository.TestRepo3Name;
            string targetRepoName = TestRepository.TestRepo2Name;

            string testChannelName = channelName;
            string source1RepoUri = isAzDoTest ? GetAzDoRepoUrl(source1RepoName) : GetRepoUrl(source1RepoName);
            string source2RepoUri = isAzDoTest ? GetAzDoRepoUrl(source2RepoName) : GetRepoUrl(source2RepoName);
            string targetRepoUri = isAzDoTest ? GetAzDoRepoUrl(targetRepoName) : GetRepoUrl(targetRepoName);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false);

            TestContext.WriteLine($"Adding a subscription from {source1RepoName} to {targetRepoName}");
            await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(testChannelName, source1RepoName, targetRepoName, targetBranch,
                UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: new List<string> { "--batchable" },
                sourceIsAzDo: isAzDoTest, targetIsAzDo: isAzDoTest);

            TestContext.WriteLine($"Adding a subscription from {source2RepoName} to {targetRepoName}");
            await using AsyncDisposableValue<string> subscription2Id = await CreateSubscriptionAsync(testChannelName, source2RepoName, targetRepoName, targetBranch,
                UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: new List<string> { "--batchable" },
                sourceIsAzDo: isAzDoTest, targetIsAzDo: isAzDoTest);

            TestContext.WriteLine("Set up build1 for intake into target repository");
            Build build1 = await CreateBuildAsync(source1RepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, sourceBuildNumber, source1Assets);
            await AddBuildToChannelAsync(build1.Id, testChannelName);

            TestContext.WriteLine("Set up build2 for intake into target repository");
            Build build2 = await CreateBuildAsync(source2RepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, sourceBuildNumber, source2Assets);
            await AddBuildToChannelAsync(build2.Id, testChannelName);

            TestContext.WriteLine("Cloning target repo to prepare the target branch");

            TemporaryDirectory reposFolder = isAzDoTest ? await CloneAzDoRepositoryAsync(targetRepoName, targetBranch) : await CloneRepositoryAsync(targetRepoName);

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

        public async Task NonBatchedGitHubFlowTestBase(string targetBranch, string channelName, IImmutableList<AssetData> source1Assets, List<DependencyDetail> expectedDependencies,
            bool allChecks = false, bool isCoherencyTest = false, IImmutableList<AssetData> childSourceAssets = null, IImmutableList<AssetData> childSourceBuildAssets = null)
        {
            string targetRepoName = TestRepository.TestRepo2Name;
            string sourceRepoName = TestRepository.TestRepo1Name;
            string childRepoName = TestRepository.TestRepo1Name;

            if (isCoherencyTest)
            {
                if (childSourceAssets == null || childSourceBuildAssets == null)
                {
                    throw new MaestroTestException("Child source and build assets must be provided for coherency tests.");
                }

                sourceRepoName = TestRepository.TestRepo2Name;
                targetRepoName = TestRepository.TestRepo3Name;
            }

            string testChannelName = channelName;
            string sourceRepoUri = GetRepoUrl(sourceRepoName);
            string targetRepoUri = GetRepoUrl(targetRepoName);
            string childSourceRepoUri = GetRepoUrl(childRepoName);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false);

            await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionForEndToEndTests(
                testChannelName, sourceRepoName, targetRepoName, targetBranch, allChecks, false);

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

                        if (allChecks)
                        {
                            await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, isCompleted: true, isUpdated: false);
                            return;
                        }

                        if (isCoherencyTest)
                        {
                            await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, isCompleted: false, isUpdated: false);
                            return;
                        }
                    }
                }
            }
        }

        public async Task NonBatchedUpdatingGitHubFlowTestBase(string targetBranch, string channelName, IImmutableList<AssetData> source1Assets, IImmutableList<AssetData> source1AssetsUpdated,
            List<DependencyDetail> expectedDependencies, List<DependencyDetail> expectedUpdatedDependencies)
        {
            string targetRepoName = TestRepository.TestRepo2Name;
            string sourceRepoName = TestRepository.TestRepo1Name;
            string sourceBranch = TestRepository.SourceBranch;

            string testChannelName = channelName;
            string sourceRepoUri = GetRepoUrl(sourceRepoName);
            string targetRepoUri = GetRepoUrl(targetRepoName);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false);

            await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionForEndToEndTests(
                testChannelName, sourceRepoName, targetRepoName, targetBranch, false, false);

            TestContext.WriteLine("Set up build for intake into target repository");
            Build build = await CreateBuildAsync(sourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, sourceBuildNumber, source1Assets);
            await AddBuildToChannelAsync(build.Id, testChannelName);

            TestContext.WriteLine("Cloning target repo to prepare the target branch");
            TemporaryDirectory reposFolder = await CloneRepositoryAsync(targetRepoName);

            using (ChangeDirectory(reposFolder.Directory))
            {
                await using (await CheckoutBranchAsync(targetBranch))
                {

                    TestContext.WriteLine("Adding dependencies to target repo");
                    await AddDependenciesToLocalRepo(reposFolder.Directory, source1Assets.ToList(), sourceRepoUri);

                    TestContext.WriteLine("Pushing branch to remote");
                    await GitCommitAsync("Add dependencies");

                    await using (await PushGitBranchAsync("origin", targetBranch))
                    {
                        TestContext.WriteLine("Trigger the dependency update");
                        await TriggerSubscriptionAsync(subscription1Id.Value);

                        TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");
                        await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, false);

                        TestContext.WriteLine("Set up another build for intake into target repository");
                        Build build2 = await CreateBuildAsync(sourceRepoUri, sourceBranch, TestRepository.CoherencyTestRepo2Commit, source2BuildNumber, source1AssetsUpdated);

                        TestContext.WriteLine("Trigger the dependency update");
                        await TriggerSubscriptionAsync(subscription1Id.Value);

                        TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");
                        await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedUpdatedDependencies, reposFolder.Directory, false, true);

                        // Then remove the second build from the channel, trigger the sub again, and it should revert back to the original dependency set
                        TestContext.Write("Remove the build from the channel and verify that the original dependencies are restored");
                        await DeleteBuildFromChannelAsync(build2.Id.ToString(), testChannelName);

                        TestContext.WriteLine("Trigger the dependency update");
                        await TriggerSubscriptionAsync(subscription1Id.Value);

                        TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");

                        await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, false, true);
                    }
                }
            }
        }

        public async Task NonBatchedUpdatingAzDoFlowTestBase(string targetBranch, string channelName, IImmutableList<AssetData> sourceAssets, IImmutableList<AssetData> updatedSourceAssets,
            List<DependencyDetail> expectedDependencies, List<DependencyDetail> expectedUpdatedDependencies)
        {
            string targetRepoName = TestRepository.TestRepo2Name;
            string sourceRepoName = TestRepository.TestRepo1Name;
            string sourceBranch = TestRepository.SourceBranch;

            string testChannelName = channelName;
            string sourceRepoUri = GetAzDoRepoUrl(sourceRepoName);
            string targetRepoUri = GetAzDoRepoUrl(targetRepoName);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false);

            await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionForEndToEndTests(
                testChannelName, sourceRepoName, targetRepoName, targetBranch, false, true);

            TestContext.WriteLine("Set up build for intake into target repository");
            Build build = await CreateBuildAsync(sourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, sourceBuildNumber, sourceAssets);
            await AddBuildToChannelAsync(build.Id, testChannelName);

            TestContext.WriteLine("Cloning target repo to prepare the target branch");
            TemporaryDirectory reposFolder = await CloneAzDoRepositoryAsync(targetRepoName, targetBranch);

            using (ChangeDirectory(reposFolder.Directory))
            {
                await using (await CheckoutBranchAsync(targetBranch))
                {
                    TestContext.WriteLine("Adding dependencies to target repo");
                    await AddDependenciesToLocalRepo(reposFolder.Directory, sourceAssets.ToList(), sourceRepoUri);

                    TestContext.WriteLine("Pushing branch to remote");
                    await GitCommitAsync("Add dependencies");

                    await using (await PushGitBranchAsync("origin", targetBranch))
                    {
                        TestContext.WriteLine("Trigger the dependency update");
                        await TriggerSubscriptionAsync(subscription1Id.Value);

                        TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");
                        await CheckNonBatchedAzDoPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, isCompleted: false, isUpdated: false);

                        TestContext.WriteLine("Set up another build for intake into target repository");
                        Build build2 = await CreateBuildAsync(sourceRepoUri, sourceBranch, TestRepository.CoherencyTestRepo2Commit, source2BuildNumber, updatedSourceAssets);

                        TestContext.WriteLine("Trigger the dependency update");
                        await TriggerSubscriptionAsync(subscription1Id.Value);

                        TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");
                        await CheckNonBatchedAzDoPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedUpdatedDependencies, reposFolder.Directory, isCompleted: false, isUpdated: true);

                        // Then remove the second build from the channel, trigger the sub again, and it should revert back to the original dependency set
                        TestContext.Write("Remove the build from the channel and verify that the original dependencies are restored");
                        await DeleteBuildFromChannelAsync(build2.Id.ToString(), testChannelName);

                        TestContext.WriteLine("Trigger the dependency update");
                        await TriggerSubscriptionAsync(subscription1Id.Value);

                        TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");
                        await CheckNonBatchedAzDoPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, isCompleted: false, isUpdated: true);
                    }
                }
            }
        }

        public async Task NonBatchedAzDoFlowTestBase(string targetBranch, string channelName, IImmutableList<AssetData> sourceAssets,
    List<DependencyDetail> expectedDependencies, bool allChecks = false, bool isFeedTest = false, string[] expectedFeeds = null, string[] notExpectedFeeds = null)
        {
            string targetRepoName = TestRepository.TestRepo2Name;
            string sourceRepoName = TestRepository.TestRepo1Name;

            string testChannelName = channelName;
            string sourceRepoUri = GetAzDoRepoUrl(sourceRepoName);
            string targetRepoUri = GetAzDoRepoUrl(targetRepoName);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false);

            await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionForEndToEndTests(
                testChannelName, sourceRepoName, targetRepoName, targetBranch, allChecks, true);

            TestContext.WriteLine("Set up build for intake into target repository");
            Build build = await CreateBuildAsync(sourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, sourceBuildNumber, sourceAssets);
            await AddBuildToChannelAsync(build.Id, testChannelName);

            TestContext.WriteLine("Cloning target repo to prepare the target branch");
            TemporaryDirectory reposFolder = await CloneAzDoRepositoryAsync(targetRepoName, targetBranch);

            using (ChangeDirectory(reposFolder.Directory))
            {
                await using (await CheckoutBranchAsync(targetBranch))
                {
                    TestContext.WriteLine("Adding dependencies to target repo");
                    await AddDependenciesToLocalRepo(reposFolder.Directory, sourceAssets.ToList(), sourceRepoUri);

                    TestContext.WriteLine("Pushing branch to remote");
                    await GitCommitAsync("Add dependencies");

                    await using (await PushGitBranchAsync("origin", targetBranch))
                    {
                        TestContext.WriteLine("Trigger the dependency update");
                        await TriggerSubscriptionAsync(subscription1Id.Value);

                        TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                        if (allChecks)
                        {
                            await CheckNonBatchedAzDoPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, isCompleted: true, isUpdated: false);
                            return;
                        }

                        if (isFeedTest)
                        {
                            await CheckNonBatchedAzDoPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, isCompleted: false, isUpdated: false, expectedFeeds: expectedFeeds, notExpectedFeeds: notExpectedFeeds);
                            return;
                        }
                    }
                }
            }
        }

        private async Task<AsyncDisposableValue<string>> CreateSubscriptionForEndToEndTests(string testChannelName, string sourceRepoName,
            string targetRepoName, string targetBranch, bool allChecks, bool isAzDoTest)
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
                    trigger: true,
                    sourceIsAzDo: isAzDoTest,
                    targetIsAzDo: isAzDoTest);
            }
            else
            {
                return await CreateSubscriptionAsync(
                    testChannelName,
                    sourceRepoName,
                    targetRepoName,
                    targetBranch,
                    UpdateFrequency.None.ToString(),
                    "maestro-auth-test",
                    sourceIsAzDo: isAzDoTest,
                    targetIsAzDo: isAzDoTest);
            }
        }
    }
}
