// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;
using Octokit;
using ProductConstructionService.ScenarioTests.Helpers;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("CodeFlow")]
internal partial class ScenarioTests_CodeFlow : CodeFlowScenarioTestBase
{
    /*
    This test does the following
    - Create an (empty) PR with awaiting conflict resolution
    - Call `darc vmr resolve-conflicts`
    - Verify mergeability
    - Push a new update into the PR
    - Make a conflicting change in the PR
    - Push a new update which will require another resolution
    - Call `darc vmr resolve-conflicts`
    - Verify mergeability
    */
    [Test]
    public async Task Vmr_ConflictNoPrForwardFlowWithRebaseTest()
    {
        var channelName = GetTestChannelName();
        var sourceBranchName = GetTestBranchName();
        var targetBranchName = GetTestBranchName();

        await CreateTestChannelAsync(channelName);

        var subscriptionId = await CreateForwardFlowSubscriptionAsync(
            channelName,
            TestRepository.TestRepo1Name,
            TestRepository.VmrTestRepoName,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            targetDirectory: TestRepository.TestRepo1Name);

        await using IAsyncDisposable _ = await EnableRebaseStrategy(subscriptionId);

        using TemporaryDirectory vmrDirectory = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        using TemporaryDirectory repoDirectory = await CloneRepositoryAsync(TestRepository.TestRepo1Name);

        var vmrDir = new NativePath(vmrDirectory.Directory);
        var repoDir = new NativePath(repoDirectory.Directory);

        var newFilePath1 = repoDir / TestFile1Name;
        var newFileInVmrPath1 = vmrDir / VmrInfo.SourceDirName / TestRepository.TestRepo1Name / TestFile1Name;
        var newFilePath2 = repoDir / TestFile2Name;
        var newFileInVmrPath2 = vmrDir / VmrInfo.SourceDirName / TestRepository.TestRepo1Name / TestFile2Name;

        await CreateTargetBranchAndExecuteTest(targetBranchName, vmrDir, async () =>
        {
            await ChangeAndPushAFile(
                vmrDir,
                newFileInVmrPath1,
                "content #1 from the VMR",
                "Add new files to VMR");

            using var _ = ChangeDirectory(repoDir);
            await using var __ = await CheckoutBranchAsync(sourceBranchName);
            await using var ___ = await PushGitBranchAsync("origin", sourceBranchName);

            await ChangeAndPushAFile(
                repoDir,
                newFilePath1,
                "content #1 from the repository",
                "Add new files");
            await ChangeAndPushAFile(
                repoDir,
                newFilePath2,
                "content #2 from the repository",
                "Add new files");

            // Create a new build from the commit and add it to a channel
            Build build = await CreateBuildAsync(
                GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                sourceBranchName,
                (await GitGetCurrentSha()).TrimEnd(),
                "1",
                []);

            await AddBuildToChannelAsync(build.Id, channelName);
            await TriggerSubscriptionAsync(subscriptionId);

            TestContext.WriteLine("Waiting for the PR to show up");
            PullRequest pr = await WaitForPullRequestComment(TestRepository.VmrTestRepoName, targetBranchName, "darc vmr resolve-conflict");

            await using IAsyncDisposable ____ = AsyncDisposable.Create(async () =>
            {
                using var _ = ChangeDirectory(vmrDir);
                try
                {
                    await DeleteBranchAsync(pr.Head.Ref);
                }
                catch {}
            });

            await VerifyCodeFlowCheck(pr, TestRepository.VmrTestRepoName, false);
            await ResolveConflict(subscriptionId, vmrDir, pr.Head.Ref, [newFileInVmrPath1]);
            (await File.ReadAllTextAsync(newFileInVmrPath2)).Should().Be("content #2 from the repository");
            await TriggerSubscriptionAsync(subscriptionId);

            // The codeflow verification checks should pass now
            pr = await WaitForUpdatedPullRequestAsync(TestRepository.VmrTestRepoName, targetBranchName);
            await VerifyCodeFlowCheck(pr, TestRepository.VmrTestRepoName, true);

            TestContext.WriteLine("Making code changes to the repo that should cause a conflict in the PR");
            await ChangeAndPushAFile(
                repoDir,
                newFilePath2,
                "content #3 but from the repository",
                "Add conflicting changes");

            build = await CreateBuildAsync(
                GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                sourceBranchName,
                (await GitGetCurrentSha()).TrimEnd(),
                "2",
                []);

            await AddBuildToChannelAsync(build.Id, channelName);
            await TriggerSubscriptionAsync(subscriptionId);

            // We verify the file got there + make a conflicting change for future
            pr = await WaitForFileContentInPullRequest(
                vmrDir,
                TestRepository.VmrTestRepoName,
                targetBranchName,
                newFileInVmrPath2,
                "content #3 but from the repository");

            using (ChangeDirectory(vmrDir))
            {
                await CheckoutRemoteRefAsync(pr.Head.Ref);
                await ChangeAndPushAFile(
                    vmrDir,
                    newFileInVmrPath2,
                    "content #3 but from the VMR",
                    "Add conflicting changes");
            }

            TestContext.WriteLine("Making code changes to the repo that should cause a conflict in the PR");
            await ChangeAndPushAFile(
                repoDir,
                newFilePath2,
                "content #4 from the repository",
                "Add conflicting changes");

            build = await CreateBuildAsync(
                GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                sourceBranchName,
                (await GitGetCurrentSha()).TrimEnd(),
                "2",
                []);

            await AddBuildToChannelAsync(build.Id, channelName);
            await TriggerSubscriptionAsync(subscriptionId);

            // This time we should get a conflict comment for the second file
            TestContext.WriteLine("Waiting for conflict comment to show up on the PR");
            pr = await WaitForPullRequestComment(TestRepository.VmrTestRepoName, targetBranchName, TestFile2Name);
            await VerifyCodeFlowCheck(pr, TestRepository.VmrTestRepoName, false);

            // We resolve the conflict manually
            await ResolveConflict(subscriptionId, vmrDir, pr.Head.Ref, [newFileInVmrPath2]);
            (await File.ReadAllTextAsync(newFileInVmrPath2)).Should().Be("content #4 from the repository");

            await TriggerSubscriptionAsync(subscriptionId);

            // The codeflow verification checks should pass now
            await VerifyCodeFlowCheck(pr, TestRepository.VmrTestRepoName, true);   
        });
    }

    /*
    This test does the following
    - Create an (empty) PR with awaiting conflict resolution
    - Call `darc vmr resolve-conflicts`
    - Verify mergeability
    - Push a new update into the PR
    - Make a conflicting change in the PR
    - Push a new update which will require another resolution
    - Call `darc vmr resolve-conflicts`
    - Verify mergeability
    */
    [Test]
    public async Task Vmr_ConflictNoPrBackFlowWithRebaseTest()
    {
        var channelName = GetTestChannelName();
        var sourceBranchName = GetTestBranchName();
        var targetBranchName = GetTestBranchName();

        await CreateTestChannelAsync(channelName);

        var subscriptionId = await CreateBackwardFlowSubscriptionAsync(
            channelName,
            TestRepository.VmrTestRepoName,
            TestRepository.TestRepo1Name,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            sourceDirectory: TestRepository.TestRepo1Name);

        await using IAsyncDisposable _ = await EnableRebaseStrategy(subscriptionId);

        using TemporaryDirectory vmrDirectory = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        using TemporaryDirectory repoDirectory = await CloneRepositoryAsync(TestRepository.TestRepo1Name);

        var vmrDir = new NativePath(vmrDirectory.Directory);
        var repoDir = new NativePath(repoDirectory.Directory);

        var newFilePath1 = repoDir / TestFile1Name;
        var newFileInVmrPath1 = vmrDir / VmrInfo.SourceDirName / TestRepository.TestRepo1Name / TestFile1Name;
        var newFilePath2 = repoDir / TestFile2Name;
        var newFileInVmrPath2 = vmrDir / VmrInfo.SourceDirName / TestRepository.TestRepo1Name / TestFile2Name;

        await CreateTargetBranchAndExecuteTest(targetBranchName, repoDir, async () =>
        {
            await ChangeAndPushAFile(
                repoDir,
                newFilePath1,
                "content #1 from the repository",
                "Add new files to repository");

            using var _ = ChangeDirectory(vmrDir);
            await using var __ = await CheckoutBranchAsync(sourceBranchName);
            await using var ___ = await PushGitBranchAsync("origin", sourceBranchName);

            await ChangeAndPushAFile(
                vmrDir,
                newFileInVmrPath1,
                "content #1 from the VMR",
                "Add new files");
            await ChangeAndPushAFile(
                vmrDir,
                newFileInVmrPath2,
                "content #2 from the VMR",
                "Add new files");

            // Create a new build from the commit and add it to a channel
            Build build = await CreateBuildAsync(
                GetGitHubRepoUrl(TestRepository.VmrTestRepoName),
                sourceBranchName,
                (await GitGetCurrentSha()).TrimEnd(),
                "1",
                []);

            await AddBuildToChannelAsync(build.Id, channelName);
            await TriggerSubscriptionAsync(subscriptionId);

            TestContext.WriteLine("Waiting for the PR to show up");
            PullRequest pr = await WaitForPullRequestComment(TestRepository.TestRepo1Name, targetBranchName, "darc vmr resolve-conflict");

            await using IAsyncDisposable ____ = AsyncDisposable.Create(async () =>
            {
                using var _ = ChangeDirectory(repoDir);
                try
                {
                    await DeleteBranchAsync(pr.Head.Ref);
                }
                catch {}
            });

            await VerifyCodeFlowCheck(pr, TestRepository.TestRepo1Name, false);
            await ResolveConflict(subscriptionId, repoDir, pr.Head.Ref, [newFilePath1]);
            (await File.ReadAllTextAsync(newFilePath2)).Should().Be("content #2 from the VMR");
            await TriggerSubscriptionAsync(subscriptionId);

            // The codeflow verification checks should pass now
            pr = await WaitForUpdatedPullRequestAsync(TestRepository.TestRepo1Name, targetBranchName);
            await VerifyCodeFlowCheck(pr, TestRepository.TestRepo1Name, true);

            TestContext.WriteLine("Making code changes to the VMR that should cause a conflict in the PR");
            await ChangeAndPushAFile(
                vmrDir,
                newFileInVmrPath2,
                "content #3 but from the VMR",
                "Add conflicting changes");

            build = await CreateBuildAsync(
                GetGitHubRepoUrl(TestRepository.VmrTestRepoName),
                sourceBranchName,
                (await GitGetCurrentSha()).TrimEnd(),
                "2",
                []);

            await AddBuildToChannelAsync(build.Id, channelName);
            await TriggerSubscriptionAsync(subscriptionId);

            // We verify the file got there + make a conflicting change for future
            pr = await WaitForFileContentInPullRequest(
                repoDir,
                TestRepository.TestRepo1Name,
                targetBranchName,
                newFilePath2,
                "content #3 but from the VMR");

            using (ChangeDirectory(repoDir))
            {
                await CheckoutRemoteRefAsync(pr.Head.Ref);
                await ChangeAndPushAFile(
                    repoDir,
                    newFilePath2,
                    "content #3 but from the repository",
                    "Add conflicting changes");
            }

            TestContext.WriteLine("Making code changes to the VMR that should cause a conflict in the PR");
            await ChangeAndPushAFile(
                vmrDir,
                newFileInVmrPath2,
                "content #4 from the VMR",
                "Add conflicting changes");

            build = await CreateBuildAsync(
                GetGitHubRepoUrl(TestRepository.VmrTestRepoName),
                sourceBranchName,
                (await GitGetCurrentSha()).TrimEnd(),
                "2",
                []);

            await AddBuildToChannelAsync(build.Id, channelName);
            await TriggerSubscriptionAsync(subscriptionId);

            // This time we should get a conflict comment for the second file
            TestContext.WriteLine("Waiting for conflict comment to show up on the PR");
            pr = await WaitForPullRequestComment(TestRepository.TestRepo1Name, targetBranchName, TestFile2Name);
            await VerifyCodeFlowCheck(pr, TestRepository.TestRepo1Name, false);

            // We resolve the conflict manually
            await ResolveConflict(subscriptionId, repoDir, pr.Head.Ref, [newFilePath2]);
            (await File.ReadAllTextAsync(newFilePath2)).Should().Be("content #4 from the VMR");

            await TriggerSubscriptionAsync(subscriptionId);

            // The codeflow verification checks should pass now
            await VerifyCodeFlowCheck(pr, TestRepository.TestRepo1Name, true);   
        });
    }

    private async Task ResolveConflict(string subscriptionId, string targetDir, string prBranch, IEnumerable<string> filesToResolve, bool useOurs = false)
    {
        using var _ = ChangeDirectory(targetDir);
        await CheckoutRemoteRefAsync(prBranch);

        await RunDarcAsync(includeConfigurationRepoParams: false, "vmr", "resolve-conflict", "--subscription", subscriptionId);

        foreach (string file in filesToResolve)
        {
            await RunGitAsync("checkout", useOurs ? "--ours" : "--theirs", file);
        }

        await GitAddAllAsync();
        await GitCommitAsync("Resolve conflict");
        await RunGitAsync("push");
    }

    private static async Task VerifyCodeFlowCheck(PullRequest pr, string targetRepoName, bool expectSucceeded)
    {
        Repository repo = await GitHubApi.Repository.Get(TestParameters.GitHubTestOrg, targetRepoName);
        List<CheckRun> checks = await WaitForPullRequestMaestroChecksAsync(pr.Url, pr.Head.Ref, repo.Id, attempts: 10);

        // Some checks may appear sooner than other, so we wait until the Codeflow verification check completes
        var stopwatch = Stopwatch.StartNew();
        while (!checks.Any(c => c.Name.Contains("Codeflow verification") && c.Conclusion != null)
            && stopwatch.Elapsed < TimeSpan.FromMinutes(2))
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            checks = await WaitForPullRequestMaestroChecksAsync(pr.Url, pr.Head.Ref, repo.Id);
        }

        CheckRun codeFlowCheck = checks.Single(c => c.Name.Contains("Codeflow verification"));
        codeFlowCheck.Conclusion.Value.Value.Should().Be(expectSucceeded ? CheckConclusion.Success : CheckConclusion.Failure);
    }

    private static async Task<IAsyncDisposable> EnableRebaseStrategy(string subscriptionId)
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

    private static async Task ChangeAndPushAFile(string repoDir, string filePath, string content, string commitMessage)
    {
        using (ChangeDirectory(repoDir))
        {
            await File.WriteAllTextAsync(filePath, content);
            await GitAddAllAsync();
            await GitCommitAsync(commitMessage);
            await RunGitAsync("push", "origin");
        }
    }
}
