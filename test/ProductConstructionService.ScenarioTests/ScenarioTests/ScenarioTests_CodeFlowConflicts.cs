﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;

namespace ProductConstructionService.ScenarioTests.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("CodeFlow")]
internal partial class ScenarioTests_CodeFlow : CodeFlowScenarioTestBase
{
    [Test]
    public async Task ConflictPrClosedTest()
    {
        var channelName = GetTestChannelName();
        var branchName = GetTestBranchName();
        var productRepo = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var targetBranchName = GetTestBranchName();

        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(channelName);

        await using AsyncDisposableValue<string> subscriptionId = await CreateForwardFlowSubscriptionAsync(
            channelName,
            TestRepository.TestRepo1Name,
            TestRepository.VmrTestRepoName,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            targetDirectory: TestRepository.TestRepo1Name);

        TemporaryDirectory vmrDirectory = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        TemporaryDirectory reposFolder = await CloneRepositoryAsync(TestRepository.TestRepo1Name);
        var newFilePath1 = Path.Combine(reposFolder.Directory, TestFile1Name);
        var newFileInVmrPath1 = Path.Combine(vmrDirectory.Directory, VmrInfo.SourceDirName, TestRepository.TestRepo1Name, TestFile1Name);
        var newFilePath2 = Path.Combine(reposFolder.Directory, TestFile2Name);
        var newFileInVmrPath2 = Path.Combine(vmrDirectory.Directory, VmrInfo.SourceDirName, TestRepository.TestRepo1Name, TestFile2Name);

        await PrepareConflictPR(
            vmrDirectory.Directory,
            reposFolder.Directory,
            branchName,
            targetBranchName,
            newFilePath1,
            newFilePath2,
            newFileInVmrPath1,
            newFileInVmrPath2,
            channelName,
            subscriptionId.Value,
            async () =>
            {
                var pr = await WaitForPullRequestAsync(TestRepository.VmrTestRepoName, targetBranchName);

                await ClosePullRequest(TestParameters.GitHubTestOrg, TestRepository.VmrTestRepoName, pr);

                using (ChangeDirectory(reposFolder.Directory))
                {
                    await CheckForwardFlowGitHubPullRequest(
                        [(TestRepository.TestRepo1Name, (await GitGetCurrentSha()).TrimEnd())],
                        TestRepository.VmrTestRepoName,
                        targetBranchName,
                        [
                            $"src/{TestRepository.TestRepo1Name}/{TestFile1Name}",
                            $"src/{TestRepository.TestRepo1Name}/{TestFile2Name}"
                        ],
                        TestFilePatches);
                }
            });
    }

    [Test]
    public async Task ConflictResolvedTest()
    {
        var channelName = GetTestChannelName();
        var branchName = GetTestBranchName();
        var productRepo = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var targetBranchName = GetTestBranchName();

        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(channelName);

        await using AsyncDisposableValue<string> subscriptionId = await CreateForwardFlowSubscriptionAsync(
            channelName,
            TestRepository.TestRepo1Name,
            TestRepository.VmrTestRepoName,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            targetDirectory: TestRepository.TestRepo1Name);

        TemporaryDirectory vmrDirectory = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        TemporaryDirectory reposFolder = await CloneRepositoryAsync(TestRepository.TestRepo1Name);
        var newFilePath1 = Path.Combine(reposFolder.Directory, TestFile1Name);
        var newFileInVmrPath1 = Path.Combine(vmrDirectory.Directory, VmrInfo.SourceDirName, TestRepository.TestRepo1Name, TestFile1Name);
        var newFilePath2 = Path.Combine(reposFolder.Directory, TestFile2Name);
        var newFileInVmrPath2 = Path.Combine(vmrDirectory.Directory, VmrInfo.SourceDirName, TestRepository.TestRepo1Name, TestFile2Name);

        await PrepareConflictPR(
            vmrDirectory.Directory,
            reposFolder.Directory,
            branchName,
            targetBranchName,
            newFilePath1,
            newFilePath2,
            newFileInVmrPath1,
            newFileInVmrPath2,
            channelName,
            subscriptionId.Value,
            async () =>
            {
                var pr = await WaitForPullRequestAsync(TestRepository.VmrTestRepoName, targetBranchName);

                using (ChangeDirectory(vmrDirectory.Directory))
                {
                    TestContext.WriteLine("Reverting the commit that caused the conflict");
                    await CheckoutRemoteRefAsync(pr.Head.Ref);
                    await RunGitAsync("revert", (await GitGetCurrentSha()).TrimEnd());
                    await RunGitAsync("push", "origin", pr.Head.Ref);
                }

                using (ChangeDirectory(reposFolder.Directory))
                {
                    await WaitForNewCommitInPullRequest(TestRepository.VmrTestRepoName, pr, 4);
                    await CheckForwardFlowGitHubPullRequest(
                        [(TestRepository.TestRepo1Name, (await GitGetCurrentSha()).TrimEnd())],
                        TestRepository.VmrTestRepoName,
                        targetBranchName,
                        [
                            $"src/{TestRepository.TestRepo1Name}/{TestFile1Name}",
                            $"src/{TestRepository.TestRepo1Name}/{TestFile2Name}"
                        ],
                        TestFilePatches);
                }
            });
    }

    private async Task PrepareConflictPR(
        string vmrDirectory,
        string productRepoDirectory,
        string sourceBranchName,
        string targetBranchName,
        string newFilePath1,
        string newFilePath2,
        string newFileInVmrPath1,
        string newFileInVmrPath2,
        string channelName,
        string subscriptionId,
        Func<Task> test)
    {
        await CreateTargetBranchAndExecuteTest(targetBranchName, vmrDirectory, async () =>
        {
            using (ChangeDirectory(productRepoDirectory))
            {
                await using (await CheckoutBranchAsync(sourceBranchName))
                {
                    // Make a change in a product repo
                    TestContext.WriteLine("Making code changes to the repo");
                    await File.WriteAllTextAsync(newFilePath1, "not important");
                    await File.WriteAllTextAsync(newFilePath2, "not important");

                    await GitAddAllAsync();
                    await GitCommitAsync("Add new files");

                    // Push it to github
                    await using (await PushGitBranchAsync("origin", sourceBranchName))
                    {
                        var repoSha = (await GitGetCurrentSha()).TrimEnd();

                        // Create a new build from the commit and add it to a channel
                        Build build = await CreateBuildAsync(
                            GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                            sourceBranchName,
                            repoSha,
                            "1",
                            []);

                        TestContext.WriteLine("Adding build to channel");
                        await AddBuildToChannelAsync(build.Id, channelName);

                        TestContext.WriteLine("Triggering the subscription");
                        await TriggerSubscriptionAsync(subscriptionId);

                        TestContext.WriteLine("Waiting for the PR to show up");
                        Octokit.PullRequest pr = await WaitForPullRequestAsync(TestRepository.VmrTestRepoName, targetBranchName);

                        // Now make a change directly in the PR
                        using (ChangeDirectory(vmrDirectory))
                        {
                            await CheckoutRemoteRefAsync(pr.Head.Ref);
                            File.WriteAllText(newFileInVmrPath1, "file edited in PR");
                            File.Delete(newFileInVmrPath2);

                            await GitAddAllAsync();
                            await GitCommitAsync("Edit files in PR");
                            await RunGitAsync("push", "-u", "origin", pr.Head.Ref);
                        }

                        // Make a change in a product repo again, it should cause a conflict
                        TestContext.WriteLine("Making code changes to the repo that should cause a conflict in the service");
                        await File.WriteAllTextAsync(newFilePath1, TestFilesContent[TestFile1Name]);
                        await File.WriteAllTextAsync(newFilePath2, TestFilesContent[TestFile2Name]);

                        await GitAddAllAsync();
                        await GitCommitAsync("Add conflicting changes");
                        await RunGitAsync("push");

                        repoSha = (await GitGetCurrentSha()).TrimEnd();
                        TestContext.WriteLine("Creating a build from the new commit");
                        build = await CreateBuildAsync(
                            GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                            sourceBranchName, 
                            repoSha,
                            "2",
                            []);

                        TestContext.WriteLine("Adding build to channel");
                        await AddBuildToChannelAsync(build.Id, channelName);

                        TestContext.WriteLine("Triggering the subscription");
                        await TriggerSubscriptionAsync(subscriptionId);

                        TestContext.WriteLine("Waiting for conflict comment to show up on the PR");
                        pr = await WaitForPullRequestComment(TestRepository.VmrTestRepoName, targetBranchName, "conflict");
                        await CheckIfPullRequestCommentExists(
                            TestRepository.VmrTestRepoName,
                            targetBranchName,
                            pr,
                            [
                                $"[{TestFile1Name}](https://github.com/{TestRepository.TestOrg}/{TestRepository.TestRepo1Name}/blob/{repoSha}/{TestFile1Name})",
                                $"[{TestFile2Name}](https://github.com/{TestRepository.TestOrg}/{TestRepository.TestRepo1Name}/blob/{repoSha}/{TestFile2Name})"
                            ]);

                        await test();
                    }
                }
            }
        });
    }
}
