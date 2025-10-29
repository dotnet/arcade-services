// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("CodeFlow")]
internal partial class ScenarioTests_CodeFlow : CodeFlowScenarioTestBase
{
    [Test]
    public async Task Vmr_ConflictNoPrForwardFlowWithRebaseTest()
    {
        var channelName = GetTestChannelName();
        var sourceBranchName = GetTestBranchName();
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

        await using IAsyncDisposable _ = await EnableRebaseStrategy(subscriptionId.Value);

        using TemporaryDirectory vmrDirectory = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        using TemporaryDirectory repoDirectory = await CloneRepositoryAsync(TestRepository.TestRepo1Name);
        var newFilePath1 = Path.Combine(repoDirectory.Directory, TestFile1Name);
        var newFileInVmrPath1 = Path.Combine(vmrDirectory.Directory, VmrInfo.SourceDirName, TestRepository.TestRepo1Name, TestFile1Name);
        var newFilePath2 = Path.Combine(repoDirectory.Directory, TestFile2Name);
        var newFileInVmrPath2 = Path.Combine(vmrDirectory.Directory, VmrInfo.SourceDirName, TestRepository.TestRepo1Name, TestFile2Name);

        await CreateTargetBranchAndExecuteTest(targetBranchName, vmrDirectory.Directory, async () =>
        {
            // Change both repo and VMR but differently
            using (ChangeDirectory(vmrDirectory.Directory))
            {
                await CheckoutRemoteBranchAsync(targetBranchName);

                TestContext.WriteLine("Making code changes to the VMR");
                await File.WriteAllTextAsync(newFileInVmrPath1, "content #1 from the VMR");
                await GitAddAllAsync();
                await GitCommitAsync("Add new files to VMR");
                await RunGitAsync("push", "origin", targetBranchName);
            }

            using (ChangeDirectory(repoDirectory.Directory))
            {
                await using (await CheckoutBranchAsync(sourceBranchName))
                {
                    TestContext.WriteLine("Making code changes to the repo");
                    await File.WriteAllTextAsync(newFilePath1, "content #1 from the repository");
                    await File.WriteAllTextAsync(newFilePath2, "content #2 from the repository");
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
                        await TriggerSubscriptionAsync(subscriptionId.Value);

                        TestContext.WriteLine("Waiting for the PR to show up");
                        Octokit.PullRequest pr = await WaitForPullRequestComment(TestRepository.VmrTestRepoName, targetBranchName, "darc vmr resolve-conflict");

                        await using IAsyncDisposable __ = AsyncDisposable.Create(async () =>
                        {
                            using (ChangeDirectory(vmrDirectory.Directory))
                                await DeleteBranchAsync(pr.Head.Ref);
                        });

                        // The codeflow verification checks should fail due to the conflict
                        var checkResult = await CheckGithubPullRequestChecks(TestRepository.VmrTestRepoName, targetBranchName, waitTime: TimeSpan.Zero);
                        checkResult.Should().BeFalse();

                        // We resolve the conflict manually
                        using (ChangeDirectory(vmrDirectory.Directory))
                        {
                            await CheckoutRemoteRefAsync(pr.Head.Ref);

                            // TODO: Resolve conflicts
                            // await RunDarcAsync("vmr", "resolve-conflict", "--subscription", subscriptionId.Value);
                            // Verify the other file made it here too
                            //(await File.ReadAllTextAsync(newFileInVmrPath2)).Should().Be("content #2 from the repository");
                            //await RunGitAsync("checkout", "--ours", newFileInVmrPath1);
                            //await GitAddAllAsync();
                            //await GitCommitAsync("Resolve conflict");
                            //await RunGitAsync("push", "-u", "origin", pr.Head.Ref);
                        }

                        await TriggerSubscriptionAsync(subscriptionId.Value);
                        // The codeflow verification checks should pass now
                        checkResult = await CheckGithubPullRequestChecks(TestRepository.VmrTestRepoName, targetBranchName, waitTime: TimeSpan.FromSeconds(60));
                        checkResult.Should().BeTrue();

                        // Make a change in a product repo again
                        TestContext.WriteLine("Making code changes to the repo that should cause a conflict in the PR");
                        await File.WriteAllTextAsync(newFilePath2, "content #3 from the repository");
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
                        await TriggerSubscriptionAsync(subscriptionId.Value);

                        await WaitForUpdatedPullRequestAsync(TestRepository.VmrTestRepoName, targetBranchName);

                        // We verify the file got there + make a conflicting change for future
                        using (ChangeDirectory(vmrDirectory.Directory))
                        {
                            await CheckoutRemoteRefAsync(pr.Head.Ref);
                            (await File.ReadAllTextAsync(newFileInVmrPath2)).Should().Be("content #3 from the repository");
                            await File.WriteAllTextAsync(newFileInVmrPath1, "content #3 but from the VMR");
                            await GitAddAllAsync();
                            await GitCommitAsync("Add new files to VMR");
                            await RunGitAsync("push", "origin", targetBranchName);
                        }

                        // Make a conflicting change in a product repo again
                        TestContext.WriteLine("Making code changes to the repo that should cause a conflict in the PR");
                        await File.WriteAllTextAsync(newFilePath2, "content #4 from the repository");
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
                        await TriggerSubscriptionAsync(subscriptionId.Value);

                        // This time we should get a conflict comment for the second file
                        TestContext.WriteLine("Waiting for conflict comment to show up on the PR");
                        pr = await WaitForPullRequestComment(TestRepository.VmrTestRepoName, targetBranchName, TestFile2Name);

                        // We resolve the conflict manually
                        using (ChangeDirectory(vmrDirectory.Directory))
                        {
                            await CheckoutRemoteRefAsync(pr.Head.Ref);

                            // TODO: Resolve conflicts
                            // await RunDarcAsync("vmr", "resolve-conflict", "--subscription", subscriptionId.Value);
                            // Verify the other file made it here too
                            //await RunGitAsync("checkout", "--ours", newFileInVmrPath2);
                            //await GitAddAllAsync();
                            //await GitCommitAsync("Resolve conflict");
                            //await RunGitAsync("push", "-u", "origin", pr.Head.Ref);
                        }

                        await TriggerSubscriptionAsync(subscriptionId.Value);
                        // The codeflow verification checks should pass now
                        checkResult = await CheckGithubPullRequestChecks(TestRepository.VmrTestRepoName, targetBranchName, waitTime: TimeSpan.FromSeconds(60));
                        checkResult.Should().BeTrue();
                    }
                }
            }
        });
    }

    private async Task<IAsyncDisposable> EnableRebaseStrategy(string subscriptionId)
    {
        await PcsApi.FeatureFlags.SetFeatureFlagAsync(new SetFeatureFlagRequest(Guid.Parse(subscriptionId))
        {
            FlagName = Common.FeatureFlag.EnableRebaseStrategy.Name,
            ExpiryDays = 1,
            Value = "true",
        });

        return AsyncDisposable.Create(async () =>
        {
            await PcsApi.FeatureFlags.RemoveFeatureFlagAsync(Common.FeatureFlag.EnableRebaseStrategy.Name, Guid.Parse(subscriptionId));
        });
    }
}
