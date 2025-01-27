// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;

#nullable enable
namespace ProductConstructionService.ScenarioTests.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("CodeFlow")]
internal class ScenarioTests_CodeFlow : CodeFlowScenarioTestBase
{
    private const string TestFileName = "newFile.txt";
    private const string DefaultPatch = "@@ -0,0 +1 @@\n+test\n\\ No newline at end of file";

    private static readonly Dictionary<string, string> TestFilesContent = new()
    {
        { TestFileName, "test" }
    };

    private static readonly Dictionary<string, string> TestFilePatches = new()
    {
        { $"{TestFileName}", DefaultPatch },
        { $"src/{TestRepository.TestRepo1Name}/{TestFileName}", DefaultPatch },
        { $"src/{TestRepository.TestRepo2Name}/{TestFileName}", DefaultPatch },
    };

    private const string CommitPlaceholder = "<commitPlaceholder>";
    private const string ConflictMessage = $"""
        There was a conflict in the PR branch when flowing source from https://github.com/maestro-auth-test/maestro-test1/tree/{CommitPlaceholder}
        Conflicting files:
         - 1newFile.txt
         - newFile.txt

        Updates from this subscription will be blocked until the conflict is resolved, or the PR is merged
        
        """;

    [Test]
    public async Task Vmr_ForwardFlowTest()
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
        var newFilePath = Path.Combine(reposFolder.Directory, TestFileName);

        await CreateTargetBranchAndExecuteTest(targetBranchName, vmrDirectory, async () =>
        {
            using (ChangeDirectory(reposFolder.Directory))
            {
                await using (await CheckoutBranchAsync(branchName))
                {
                    // Make a change in a product repo
                    TestContext.WriteLine("Making code changes to the repo");
                    await File.WriteAllTextAsync(newFilePath, TestFilesContent[TestFileName]);

                    await GitAddAllAsync();
                    await GitCommitAsync("Add new file");

                    // Push it to github
                    await using (await PushGitBranchAsync("origin", branchName))
                    {
                        var repoSha = (await GitGetCurrentSha()).TrimEnd();

                        // Create a new build from the commit and add it to a channel
                        Build build = await CreateBuildAsync(
                            GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                            branchName,
                            repoSha,
                            "1",
                            []);

                        TestContext.WriteLine("Adding build to channel");
                        await AddBuildToChannelAsync(build.Id, channelName);

                        TestContext.WriteLine("Triggering the subscription");
                        // Now trigger the subscription
                        await TriggerSubscriptionAsync(subscriptionId.Value);

                        TestContext.WriteLine("Verifying subscription PR");
                        await CheckForwardFlowGitHubPullRequest(
                            [(TestRepository.TestRepo1Name, repoSha)],
                            TestRepository.VmrTestRepoName,
                            targetBranchName,
                            [$"src/{TestRepository.TestRepo1Name}/{TestFileName}"],
                            TestFilePatches);
                    }
                }
            }
        });
    }

    [Test]
    public async Task Vmr_BackwardFlowTest()
    {
        var channelName = GetTestChannelName();
        var branchName = GetTestBranchName();
        var productRepo = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var targetBranchName = GetTestBranchName();

        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(channelName);

        await using AsyncDisposableValue<string> subscriptionId = await CreateBackwardFlowSubscriptionAsync(
            channelName,
            TestRepository.VmrTestRepoName,
            TestRepository.TestRepo1Name,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            sourceDirectory: TestRepository.TestRepo1Name);

        TemporaryDirectory testRepoFolder = await CloneRepositoryAsync(TestRepository.TestRepo1Name);
        TemporaryDirectory reposFolder = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        var newFilePath = Path.Combine(reposFolder.Directory, "src", TestRepository.TestRepo1Name, TestFileName);

        await CreateTargetBranchAndExecuteTest(targetBranchName, testRepoFolder, async () =>
        {
            using (ChangeDirectory(reposFolder.Directory))
            {
                await using (await CheckoutBranchAsync(branchName))
                {
                    // Make a change in the VMR
                    TestContext.WriteLine("Making code changes in the VMR");
                    File.WriteAllText(newFilePath, TestFilesContent[TestFileName]);

                    await GitAddAllAsync();
                    await GitCommitAsync("Add new file");

                    // Push it to github
                    await using (await PushGitBranchAsync("origin", branchName))
                    {
                        var repoSha = (await GitGetCurrentSha()).TrimEnd();

                        // Create a new build from the commit and add it to a channel
                        Build build = await CreateBuildAsync(
                            GetGitHubRepoUrl(TestRepository.VmrTestRepoName),
                            branchName,
                            repoSha,
                            "1",
                            // We might want to add some assets here to mimic what happens in the VMR
                            []);

                        TestContext.WriteLine("Adding build to channel");
                        await AddBuildToChannelAsync(build.Id, channelName);

                        TestContext.WriteLine("Triggering the subscription");
                        // Now trigger the subscription
                        await TriggerSubscriptionAsync(subscriptionId.Value);

                        TestContext.WriteLine("Verifying subscription PR");
                        await CheckBackwardFlowGitHubPullRequest(
                            TestRepository.VmrTestRepoName,
                            TestRepository.TestRepo1Name,
                            targetBranchName,
                            [TestFileName],
                            TestFilePatches,
                            repoSha,
                            build.Id);
                    }
                }
            }
        });
    }

    [Test]
    public async Task Vmr_BatchedForwardFlowTest()
    {
        var channelName = GetTestChannelName();
        var branch1Name = GetTestBranchName();
        var branch2Name = GetTestBranchName();
        var productRepo = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var targetBranchName = GetTestBranchName();

        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(channelName);

        await using AsyncDisposableValue<string> subscription1Id = await CreateForwardFlowSubscriptionAsync(
            channelName,
            TestRepository.TestRepo1Name,
            TestRepository.VmrTestRepoName,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            targetDirectory: TestRepository.TestRepo1Name,
            batchable: true);

        await using AsyncDisposableValue<string> subscription2Id = await CreateForwardFlowSubscriptionAsync(
            channelName,
            TestRepository.TestRepo2Name,
            TestRepository.VmrTestRepoName,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            targetDirectory: TestRepository.TestRepo2Name,
            batchable: true);

        TemporaryDirectory vmrDirectory = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        TemporaryDirectory repo1 = await CloneRepositoryAsync(TestRepository.TestRepo1Name);
        TemporaryDirectory repo2 = await CloneRepositoryAsync(TestRepository.TestRepo2Name);
        var newFile1Path = Path.Combine(repo1.Directory, TestFileName);
        var newFile2Path = Path.Combine(repo2.Directory, TestFileName);

        await CreateTargetBranchAndExecuteTest(targetBranchName, vmrDirectory, async () =>
        {
            using (ChangeDirectory(repo1.Directory))
            await using (await CheckoutBranchAsync(branch1Name))
            {
                // Make a change in a product repo
                TestContext.WriteLine("Making code changes to the repo");
                await File.WriteAllTextAsync(newFile1Path, TestFilesContent[TestFileName]);

                await GitAddAllAsync();
                await GitCommitAsync("Add new file");
                var repo1Sha = (await GitGetCurrentSha()).TrimEnd();

                // Push it to github
                await using (await PushGitBranchAsync("origin", branch1Name))
                {
                    using (ChangeDirectory(repo2.Directory))
                    await using (await CheckoutBranchAsync(branch2Name))
                    {
                        // Make a change in a product repo
                        TestContext.WriteLine("Making code changes to the repo");
                        await File.WriteAllTextAsync(newFile2Path, TestFilesContent[TestFileName]);

                        await GitAddAllAsync();
                        await GitCommitAsync("Add new file");
                        var repo2Sha = (await GitGetCurrentSha()).TrimEnd();

                        // Push it to github
                        await using (await PushGitBranchAsync("origin", branch2Name))
                        {

                            // Create a new build from the commit and add it to a channel
                            Build build1 = await CreateBuildAsync(
                                GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                                branch1Name,
                                repo1Sha,
                                "B1",
                                []);

                            Build build2 = await CreateBuildAsync(
                                GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                                branch2Name,
                                repo2Sha,
                                "B2",
                                []);

                            TestContext.WriteLine("Adding builds to channel");
                            await AddBuildToChannelAsync(build1.Id, channelName);
                            await AddBuildToChannelAsync(build2.Id, channelName);

                            TestContext.WriteLine("Triggering the subscriptions");
                            // Now trigger the subscriptions
                            await TriggerSubscriptionAsync(subscription1Id.Value);
                            await TriggerSubscriptionAsync(subscription2Id.Value);

                            TestContext.WriteLine("Verifying the PR");
                            await CheckForwardFlowGitHubPullRequest(
                                [
                                    (TestRepository.TestRepo1Name, repo1Sha),
                                    (TestRepository.TestRepo2Name, repo2Sha),
                                ],
                                TestRepository.VmrTestRepoName,
                                targetBranchName,
                                [
                                    $"src/{TestRepository.TestRepo1Name}/{TestFileName}",
                                    $"src/{TestRepository.TestRepo2Name}/{TestFileName}"
                                ],
                                TestFilePatches);
                        }
                    }
                }
            }
        });
    }

    [Test]
    public async Task Conflict()
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
        var newFilePath = Path.Combine(reposFolder.Directory, TestFileName);
        var newFileInVmrPath = Path.Combine(vmrDirectory.Directory, VmrInfo.SourceDirName, TestRepository.TestRepo1Name, TestFileName);
        var newFilePath2 = Path.Combine(reposFolder.Directory, "1" + TestFileName);
        var newFileInVmrPath2 = Path.Combine(vmrDirectory.Directory, VmrInfo.SourceDirName, TestRepository.TestRepo1Name, "1" + TestFileName);

        await CreateTargetBranchAndExecuteTest(targetBranchName, vmrDirectory, async () =>
        {
            using (ChangeDirectory(reposFolder.Directory))
            {
                await using (await CheckoutBranchAsync(branchName))
                {
                    // Make a change in a product repo
                    TestContext.WriteLine("Making code changes to the repo");
                    await File.WriteAllTextAsync(newFilePath, TestFilesContent[TestFileName]);
                    await File.WriteAllTextAsync(newFilePath2, "just a second fileasd");

                    await GitAddAllAsync();
                    await GitCommitAsync("Add new file");

                    // Push it to github
                    await using (await PushGitBranchAsync("origin", branchName))
                    {
                        var repoSha = (await GitGetCurrentSha()).TrimEnd();

                        // Create a new build from the commit and add it to a channel
                        Build build = await CreateBuildAsync(
                            GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                            branchName,
                            repoSha,
                            "1",
                            []);

                        TestContext.WriteLine("Adding build to channel");
                        await AddBuildToChannelAsync(build.Id, channelName);

                        TestContext.WriteLine("Triggering the subscription");
                        await TriggerSubscriptionAsync(subscriptionId.Value);

                        TestContext.WriteLine("Waiting for the PR to show up");
                        Octokit.PullRequest pr = await WaitForPullRequestAsync(TestRepository.VmrTestRepoName, targetBranchName);

                        // Now make a change directly in the PR
                        using (ChangeDirectory(vmrDirectory.Directory))
                        {
                            await CheckoutRemoteRefAsync(pr.Head.Ref);
                            File.WriteAllText(newFileInVmrPath, "file edited in PR");
                            File.Delete(newFileInVmrPath2);

                            await GitAddAllAsync();
                            await GitCommitAsync("Edit file in PR");
                            await RunGitAsync("push", "-u", "origin", pr.Head.Ref);
                        }

                        // Make a change in a product repo again, it should cause a conflict
                        TestContext.WriteLine("Making code changes to the repo that should cause a conflict in the service");
                        await File.WriteAllTextAsync(newFilePath, "conflicting changes");
                        await File.WriteAllTextAsync(newFilePath2, "just a second file");

                        await GitAddAllAsync();
                        await GitCommitAsync("Add conflicting changes");
                        await RunGitAsync("push");

                        repoSha = (await GitGetCurrentSha()).TrimEnd();
                        TestContext.WriteLine("Creating a build from the new commit");
                        build = await CreateBuildAsync(
                            GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                            branchName,
                            repoSha,
                            "2",
                            []);

                        TestContext.WriteLine("Adding build to channel");
                        await AddBuildToChannelAsync(build.Id, channelName);

                        TestContext.WriteLine("Triggering the subscription");
                        await TriggerSubscriptionAsync(subscriptionId.Value);

                        TestContext.WriteLine("Waiting for conflict comment to show up on the PR");

                        pr = await WaitForUpdatedPullRequestAsync(TestRepository.VmrTestRepoName, targetBranchName);
                        await CheckConflictPullRequestComment(
                            TestRepository.VmrTestRepoName,
                            targetBranchName,
                            pr,
                            ConflictMessage.Replace(CommitPlaceholder, repoSha));

                        await ClosePullRequest(TestParameters.GitHubTestOrg, TestRepository.VmrTestRepoName, pr);

                        pr = await WaitForPullRequestAsync(TestRepository.VmrTestRepoName, targetBranchName);
                    }
                }
            }
        });
    }
}
