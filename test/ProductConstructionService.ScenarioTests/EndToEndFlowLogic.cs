// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Microsoft.DotNet.DarcLib;
using NUnit.Framework;
using ProductConstructionService.Client.Models;

namespace ProductConstructionService.ScenarioTests;

internal class EndToEndFlowLogic : ScenarioTestBase
{
    private const string SourceBuildNumber = "654321";
    private const string Source2BuildNumber = "987654";
    private readonly TestParameters _parameters;

    public EndToEndFlowLogic(TestParameters parameters)
    {
        _parameters = parameters;
        SetTestParameters(_parameters);
    }

    public async Task DarcBatchedFlowTestBase(
        string targetBranch,
        string channelName,
        IImmutableList<AssetData> source1Assets,
        IImmutableList<AssetData> source2Assets,
        List<DependencyDetail> expectedDependencies,
        bool isAzDoTest)
    {
        var source1RepoName = TestRepository.TestRepo1Name;
        var source2RepoName = TestRepository.TestRepo3Name;
        var targetRepoName = TestRepository.TestRepo2Name;

        var testChannelName = channelName;
        var source1RepoUri = isAzDoTest ? GetAzDoRepoUrl(source1RepoName) : GetGitHubRepoUrl(source1RepoName);
        var source2RepoUri = isAzDoTest ? GetAzDoRepoUrl(source2RepoName) : GetGitHubRepoUrl(source2RepoName);
        var targetRepoUri = isAzDoTest ? GetAzDoRepoUrl(targetRepoName) : GetGitHubRepoUrl(targetRepoName);

        TestContext.WriteLine($"Creating test channel {testChannelName}");
        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName);

        TestContext.WriteLine($"Adding a subscription from {source1RepoName} to {targetRepoName}");
        await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(testChannelName, source1RepoName, targetRepoName, targetBranch,
            UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: ["--batchable"],
            sourceIsAzDo: isAzDoTest, targetIsAzDo: isAzDoTest);

        TestContext.WriteLine($"Adding a subscription from {source2RepoName} to {targetRepoName}");
        await using AsyncDisposableValue<string> subscription2Id = await CreateSubscriptionAsync(testChannelName, source2RepoName, targetRepoName, targetBranch,
            UpdateFrequency.None.ToString(), "maestro-auth-test", additionalOptions: ["--batchable"],
            sourceIsAzDo: isAzDoTest, targetIsAzDo: isAzDoTest);

        TestContext.WriteLine("Set up build1 for intake into target repository");
        Build build1 = await CreateBuildAsync(source1RepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, SourceBuildNumber, source1Assets);
        await AddBuildToChannelAsync(build1.Id, testChannelName);

        TestContext.WriteLine("Set up build2 for intake into target repository");
        Build build2 = await CreateBuildAsync(source2RepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, SourceBuildNumber, source2Assets);
        await AddBuildToChannelAsync(build2.Id, testChannelName);

        TestContext.WriteLine("Cloning target repo to prepare the target branch");

        TemporaryDirectory reposFolder = isAzDoTest ? await CloneAzDoRepositoryAsync(targetRepoName) : await CloneRepositoryAsync(targetRepoName);

        using (ChangeDirectory(reposFolder.Directory))
        {
            await using (await CheckoutBranchAsync(targetBranch))
            {
                TestContext.WriteLine("Adding dependencies to target repo");
                await AddDependenciesToLocalRepo(reposFolder.Directory, [.. source1Assets], targetRepoUri);
                await AddDependenciesToLocalRepo(reposFolder.Directory, [.. source2Assets], targetRepoUri);

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
                        await CheckBatchedAzDoPullRequest([source1RepoName, source2RepoName], targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory);
                    }
                    else
                    {
                        await CheckBatchedGitHubPullRequest(targetBranch, [source1RepoName, source2RepoName], targetRepoName, expectedDependencies, reposFolder.Directory);
                    }
                }
            }
        }
    }

    public async Task NonBatchedGitHubFlowTestBase(string targetBranch, string channelName, IImmutableList<AssetData> sourceAssets,
        List<DependencyDetail> expectedDependencies, bool allChecks = false)
    {
        var targetRepoName = TestRepository.TestRepo2Name;
        var sourceRepoName = TestRepository.TestRepo1Name;

        var testChannelName = channelName;
        var sourceRepoUri = GetGitHubRepoUrl(sourceRepoName);
        var targetRepoUri = GetGitHubRepoUrl(targetRepoName);

        TestContext.WriteLine($"Creating test channel {testChannelName}");
        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName);

        await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionForEndToEndTests(
            testChannelName, sourceRepoName, targetRepoName, targetBranch, allChecks, false);

        TestContext.WriteLine("Set up build for intake into target repository");
        Build build = await CreateBuildAsync(sourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, SourceBuildNumber, sourceAssets);
        await AddBuildToChannelAsync(build.Id, testChannelName);

        TestContext.WriteLine("Cloning target repo to prepare the target branch");
        TemporaryDirectory reposFolder = await CloneRepositoryAsync(targetRepoName);

        using (ChangeDirectory(reposFolder.Directory))
        {
            await using (await CheckoutBranchAsync(targetBranch))
            {
                TestContext.WriteLine("Adding dependencies to target repo");
                await AddDependenciesToLocalRepo(reposFolder.Directory, [.. sourceAssets], sourceRepoUri);

                TestContext.WriteLine("Pushing branch to remote");
                await GitCommitAsync("Add dependencies");

                await using (await PushGitBranchAsync("origin", targetBranch))
                {
                    TestContext.WriteLine("Trigger the dependency update");
                    await TriggerSubscriptionAsync(subscription1Id.Value);

                    TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, isCompleted: true, isUpdated: false);
                }
            }
        }
    }

    public async Task NonBatchedGitHubFlowCoherencyTestBase(string targetBranch, string channelName, IImmutableList<AssetData> sourceAssets,
        IImmutableList<AssetData> childSourceAssets, List<DependencyDetail> expectedDependencies, string coherentParent, bool allChecks)
    {
        var targetRepoName = TestRepository.TestRepo3Name;
        var sourceRepoName = TestRepository.TestRepo2Name;
        var childRepoName = TestRepository.TestRepo1Name;

        var testChannelName = channelName;
        var sourceRepoUri = GetGitHubRepoUrl(sourceRepoName);
        var targetRepoUri = GetGitHubRepoUrl(targetRepoName);
        var childSourceRepoUri = GetGitHubRepoUrl(childRepoName);

        TestContext.WriteLine($"Creating test channel {testChannelName}");
        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName);

        await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionForEndToEndTests(
            testChannelName, sourceRepoName, targetRepoName, targetBranch, allChecks, false);

        TestContext.WriteLine("Set up build for intake into target repository");
        Build build = await CreateBuildAsync(sourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, SourceBuildNumber, sourceAssets);
        await AddBuildToChannelAsync(build.Id, testChannelName);

        Build build2 = await CreateBuildAsync(childSourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo2Commit,
            Source2BuildNumber, childSourceAssets);
        await AddBuildToChannelAsync(build2.Id, testChannelName);

        TestContext.WriteLine("Cloning target repo to prepare the target branch");
        TemporaryDirectory reposFolder = await CloneRepositoryAsync(targetRepoName);

        using (ChangeDirectory(reposFolder.Directory))
        {
            await using (await CheckoutBranchAsync(targetBranch))
            {
                TestContext.WriteLine("Adding dependencies to target repo");
                await AddDependenciesToLocalRepo(reposFolder.Directory, [.. sourceAssets], sourceRepoUri);

                await AddDependenciesToLocalRepo(reposFolder.Directory, [.. childSourceAssets], childSourceRepoUri, coherentParent);

                TestContext.WriteLine("Pushing branch to remote");
                await GitCommitAsync("Add dependencies");

                await using (await PushGitBranchAsync("origin", targetBranch))
                {
                    TestContext.WriteLine("Trigger the dependency update");
                    await TriggerSubscriptionAsync(subscription1Id.Value);

                    TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, isCompleted: allChecks, isUpdated: false);
                }
            }
        }
    }

    public async Task NonBatchedGitHubFlowCoherencyOnlyTestBase(string targetBranch, string channelName, IImmutableList<AssetData> sourceAssets,
        IImmutableList<AssetData> childSourceAssets, List<DependencyDetail> expectedNonCoherencyDependencies,
        List<DependencyDetail> expectedCoherencyDependencies, string coherentParent)
    {
        var targetRepoName = TestRepository.TestRepo3Name;
        var sourceRepoName = TestRepository.TestRepo2Name;
        var childRepoName = TestRepository.TestRepo1Name;

        var testChannelName = channelName;
        var sourceRepoUri = GetGitHubRepoUrl(sourceRepoName);
        var targetRepoUri = GetGitHubRepoUrl(targetRepoName);
        var childSourceRepoUri = GetGitHubRepoUrl(childRepoName);

        TestContext.WriteLine($"Creating test channel {testChannelName}");
        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName);

        await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionAsync(
            testChannelName,
            sourceRepoName,
            targetRepoName,
            targetBranch,
            UpdateFrequency.None.ToString(),
            "maestro-auth-test",
            additionalOptions: ["--validate-coherency"],
            trigger: true,
            sourceIsAzDo: false,
            targetIsAzDo: false);

        TestContext.WriteLine("Set up build for intake into target repository");
        Build build1 = await CreateBuildAsync(sourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit,
            SourceBuildNumber, sourceAssets);
        await AddBuildToChannelAsync(build1.Id, testChannelName);

        Build build2 = await CreateBuildAsync(childSourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo2Commit,
            Source2BuildNumber, childSourceAssets);
        await AddBuildToChannelAsync(build2.Id, testChannelName);

        TestContext.WriteLine("Cloning target repo to prepare the target branch");
        TemporaryDirectory reposFolder = await CloneRepositoryAsync(targetRepoName);

        using (ChangeDirectory(reposFolder.Directory))
        {
            await using (await CheckoutBranchAsync(targetBranch))
            {
                TestContext.WriteLine("Adding dependencies to target repo");
                await AddDependenciesToLocalRepo(reposFolder.Directory, [.. sourceAssets], sourceRepoUri);

                TestContext.WriteLine("Pushing branch to remote");
                await GitCommitAsync("Add dependencies 1");

                await using (await PushGitBranchAsync("origin", targetBranch))
                {
                    TestContext.WriteLine("Trigger the dependency update");
                    await TriggerSubscriptionAsync(subscription1Id.Value);

                    TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedNonCoherencyDependencies, reposFolder.Directory, isCompleted: true, isUpdated: false);

                    await RunGitAsync("checkout", targetBranch);
                    await RunGitAsync("pull", "origin", targetBranch);

                    await AddDependenciesToLocalRepo(reposFolder.Directory, [.. childSourceAssets], childSourceRepoUri, coherentParent);
                    await GitCommitAsync("Add dependencies 2");

                    await using (await PushGitBranchAsync("origin", targetBranch))
                    {
                        TestContext.WriteLine("Trigger the dependency update");
                        await TriggerSubscriptionAsync(subscription1Id.Value);

                        TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");

                        var expectedPRTitle = $"[{targetBranch}] Update dependencies to ensure coherency";
                        await CheckGitHubPullRequest(expectedPRTitle, targetRepoName, targetBranch, expectedCoherencyDependencies, reposFolder.Directory, isCompleted: false, isUpdated: false);
                    }
                }
            }
        }
    }

    public async Task NonBatchedUpdatingGitHubFlowTestBase(string targetBranch, string channelName, IImmutableList<AssetData> source1Assets, IImmutableList<AssetData> source1AssetsUpdated,
        List<DependencyDetail> expectedDependencies, List<DependencyDetail> expectedUpdatedDependencies, bool allChecks = false)
    {
        var targetRepoName = TestRepository.TestRepo2Name;
        var sourceRepoName = TestRepository.TestRepo1Name;
        var sourceBranch = TestRepository.SourceBranch;

        var testChannelName = channelName;
        var sourceRepoUri = GetGitHubRepoUrl(sourceRepoName);
        var targetRepoUri = GetGitHubRepoUrl(targetRepoName);

        TestContext.WriteLine($"Creating test channel {testChannelName}");
        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName);

        await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionForEndToEndTests(
            testChannelName, sourceRepoName, targetRepoName, targetBranch, allChecks, false);

        TestContext.WriteLine("Set up build for intake into target repository");
        Build build = await CreateBuildAsync(sourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, SourceBuildNumber, source1Assets);
        await AddBuildToChannelAsync(build.Id, testChannelName);

        TestContext.WriteLine("Cloning target repo to prepare the target branch");
        TemporaryDirectory reposFolder = await CloneRepositoryAsync(targetRepoName);

        using (ChangeDirectory(reposFolder.Directory))
        {
            await using (await CheckoutBranchAsync(targetBranch))
            {

                TestContext.WriteLine("Adding dependencies to target repo");
                await AddDependenciesToLocalRepo(reposFolder.Directory, [.. source1Assets], sourceRepoUri);

                TestContext.WriteLine("Pushing branch to remote");
                await GitCommitAsync("Add dependencies");

                await using (await PushGitBranchAsync("origin", targetBranch))
                {
                    TestContext.WriteLine("Trigger the dependency update");
                    await TriggerSubscriptionAsync(subscription1Id.Value);

                    TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");
                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, allChecks);

                    TestContext.WriteLine("Set up another build for intake into target repository");
                    Build build2 = await CreateBuildAsync(sourceRepoUri, sourceBranch, TestRepository.CoherencyTestRepo2Commit, Source2BuildNumber, source1AssetsUpdated);
                    await AddBuildToChannelAsync(build2.Id, testChannelName);

                    TestContext.WriteLine("Trigger the dependency update");
                    await TriggerSubscriptionAsync(subscription1Id.Value);

                    TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");
                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedUpdatedDependencies, reposFolder.Directory, allChecks, true);

                    // Then remove the second build from the channel, trigger the sub again, and it should revert back to the original dependency set
                    TestContext.Write("Remove the build from the channel and verify that the original dependencies are restored");
                    await DeleteBuildFromChannelAsync(build2.Id.ToString(), testChannelName);

                    TestContext.WriteLine("Trigger the dependency update");
                    await TriggerSubscriptionAsync(subscription1Id.Value);

                    TestContext.WriteLine($"Waiting for PR to be updated in {targetRepoUri}");

                    await CheckNonBatchedGitHubPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, allChecks, true);
                }
            }
        }
    }

    public async Task NonBatchedUpdatingAzDoFlowTestBase(string targetBranch, string channelName, IImmutableList<AssetData> sourceAssets, IImmutableList<AssetData> updatedSourceAssets,
        List<DependencyDetail> expectedDependencies, List<DependencyDetail> expectedUpdatedDependencies)
    {
        var targetRepoName = TestRepository.TestRepo2Name;
        var sourceRepoName = TestRepository.TestRepo1Name;
        var sourceBranch = TestRepository.SourceBranch;

        var testChannelName = channelName;
        var sourceRepoUri = GetAzDoRepoUrl(sourceRepoName);
        var targetRepoUri = GetAzDoRepoUrl(targetRepoName);

        TestContext.WriteLine($"Creating test channel {testChannelName}");
        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName);

        await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionForEndToEndTests(
            testChannelName, sourceRepoName, targetRepoName, targetBranch, false, true);

        TestContext.WriteLine("Set up build for intake into target repository");
        Build build = await CreateBuildAsync(sourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, SourceBuildNumber, sourceAssets);
        await AddBuildToChannelAsync(build.Id, testChannelName);

        TestContext.WriteLine("Cloning target repo to prepare the target branch");
        TemporaryDirectory reposFolder = await CloneAzDoRepositoryAsync(targetRepoName);

        using (ChangeDirectory(reposFolder.Directory))
        {
            await using (await CheckoutBranchAsync(targetBranch))
            {
                TestContext.WriteLine("Adding dependencies to target repo");
                await AddDependenciesToLocalRepo(reposFolder.Directory, [.. sourceAssets], sourceRepoUri);

                TestContext.WriteLine("Pushing branch to remote");
                await GitCommitAsync("Add dependencies");

                await using (await PushGitBranchAsync("origin", targetBranch))
                {
                    TestContext.WriteLine("Trigger the dependency update");
                    await TriggerSubscriptionAsync(subscription1Id.Value);

                    TestContext.WriteLine($"Waiting on PR to be opened in {targetRepoUri}");
                    await CheckNonBatchedAzDoPullRequest(sourceRepoName, targetRepoName, targetBranch, expectedDependencies, reposFolder.Directory, isCompleted: false, isUpdated: false);

                    TestContext.WriteLine("Set up another build for intake into target repository");
                    Build build2 = await CreateBuildAsync(sourceRepoUri, sourceBranch, TestRepository.CoherencyTestRepo2Commit, Source2BuildNumber, updatedSourceAssets);

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
        var targetRepoName = TestRepository.TestRepo2Name;
        var sourceRepoName = TestRepository.TestRepo1Name;

        var testChannelName = channelName;
        var sourceRepoUri = GetAzDoRepoUrl(sourceRepoName);
        var targetRepoUri = GetAzDoRepoUrl(targetRepoName);

        TestContext.WriteLine($"Creating test channel {testChannelName}");
        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(testChannelName);

        await using AsyncDisposableValue<string> subscription1Id = await CreateSubscriptionForEndToEndTests(
            testChannelName, sourceRepoName, targetRepoName, targetBranch, allChecks, true);

        TestContext.WriteLine("Set up build for intake into target repository");
        Build build = await CreateBuildAsync(sourceRepoUri, TestRepository.SourceBranch, TestRepository.CoherencyTestRepo1Commit, SourceBuildNumber, sourceAssets);
        await AddBuildToChannelAsync(build.Id, testChannelName);

        TestContext.WriteLine("Cloning target repo to prepare the target branch");
        TemporaryDirectory reposFolder = await CloneAzDoRepositoryAsync(targetRepoName);

        using (ChangeDirectory(reposFolder.Directory))
        {
            await using (await CheckoutBranchAsync(targetBranch))
            {
                TestContext.WriteLine("Adding dependencies to target repo");
                await AddDependenciesToLocalRepo(reposFolder.Directory, [.. sourceAssets], sourceRepoUri);

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
                additionalOptions: ["--all-checks-passed", "--validate-coherency", "--ignore-checks", "license/cla"],
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
