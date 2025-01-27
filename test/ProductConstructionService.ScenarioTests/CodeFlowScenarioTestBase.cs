// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using FluentAssertions;
using Octokit;

#nullable enable
namespace ProductConstructionService.ScenarioTests;

internal class CodeFlowScenarioTestBase : ScenarioTestBase
{
    protected async Task CheckForwardFlowGitHubPullRequest(
        string sourceRepoName,
        string targetRepoName,
        string targetBranch,
        string[] testFiles,
        Dictionary<string, string> testFilePatches)
    {
        PullRequest pullRequest = await WaitForPullRequestAsync(targetRepoName, targetBranch);

        await using (CleanUpPullRequestAfter(TestParameters.GitHubTestOrg, targetRepoName, pullRequest))
        {
            IReadOnlyList<PullRequestFile> files = await GitHubApi.PullRequest.Files(TestParameters.GitHubTestOrg, targetRepoName, pullRequest.Number);

            files.Count.Should().Be(testFiles.Length + 2);

            // Verify source-manifest has changes
            var sourceManifestFile = files.FirstOrDefault(file => file.FileName == "src/source-manifest.json");
            sourceManifestFile.Should().NotBeNull();

            var repoPropsFile = files.FirstOrDefault(file => file.FileName == $"prereqs/git-info/{sourceRepoName}.props");
            repoPropsFile.Should().NotBeNull();

            // Verify new files are in the PR
            foreach (var testFile in testFiles)
            {
                var newFile = files.FirstOrDefault(file => file.FileName == $"src/{sourceRepoName}/{testFile}");
                newFile.Should().NotBeNull();
                newFile!.Patch.Should().Be(testFilePatches[testFile]);
            }
        }
    }

    protected async Task CheckBackwardFlowGitHubPullRequest(
        string sourceRepoName,
        string targetRepoName,
        string targetBranch,
        string[] testFiles,
        Dictionary<string, string> testFilePatches,
        string commitSha,
        int buildId)
    {
        PullRequest pullRequest = await WaitForPullRequestAsync(targetRepoName, targetBranch);

        await using (CleanUpPullRequestAfter(TestParameters.GitHubTestOrg, targetRepoName, pullRequest))
        {
            IReadOnlyList<PullRequestFile> files = await GitHubApi.PullRequest.Files(TestParameters.GitHubTestOrg, targetRepoName, pullRequest.Number);

            var versionDetailsFile = files.FirstOrDefault(file => file.FileName == "eng/Version.Details.xml");
            versionDetailsFile.Should().NotBeNull();
            versionDetailsFile!.Patch.Should().Contain(GetExpectedCodeFlowDependencyVersionEntry(sourceRepoName, commitSha, buildId));

            // Verify new files are in the PR
            foreach (var testFile in testFiles)
            {
                var newFile = files.FirstOrDefault(file => file.FileName == $"{testFile}");
                newFile.Should().NotBeNull();
                newFile!.Patch.Should().Be(testFilePatches[testFile]);
            }
        }
    }

    protected static async Task<AsyncDisposableValue<string>> CreateForwardFlowSubscriptionAsync(
        string sourceChannelName,
        string sourceRepo,
        string targetRepo,
        string targetBranch,
        string updateFrequency,
        string sourceOrg,
        string targetDirectory)
            => await CreateSourceEnabledSubscriptionAsync(
                sourceChannelName,
                sourceRepo,
                targetRepo,
                targetBranch,
                updateFrequency,
                sourceOrg,
                targetDirectory: targetDirectory);

    protected static async Task<AsyncDisposableValue<string>> CreateBackwardFlowSubscriptionAsync(
        string sourceChannelName,
        string sourceRepo,
        string targetRepo,
        string targetBranch,
        string updateFrequency,
        string sourceOrg,
        string sourceDirectory)
            => await CreateSourceEnabledSubscriptionAsync(
                sourceChannelName,
                sourceRepo,
                targetRepo,
                targetBranch,
                updateFrequency,
                sourceOrg,
                sourceDirectory: sourceDirectory);

    private static async Task<AsyncDisposableValue<string>> CreateSourceEnabledSubscriptionAsync(
        string sourceChannelName,
        string sourceRepo,
        string targetRepo,
        string targetBranch,
        string updateFrequency,
        string sourceOrg = "dotnet",
        bool sourceIsAzDo = false,
        bool targetIsAzDo = false,
        bool trigger = false,
        string? sourceDirectory = null,
        string? targetDirectory = null)
    {
        string directoryType;
        string directoryName;
        if (!string.IsNullOrEmpty(sourceDirectory))
        {
            directoryType = "--source-directory";
            directoryName = sourceDirectory;
        }
        else
        {
            directoryType = "--target-directory";
            directoryName = targetDirectory!;
        }

        return await CreateSubscriptionAsync(
                sourceChannelName,
                sourceRepo,
                targetRepo,
                targetBranch,
                updateFrequency,
                sourceOrg,
                [
                    "--source-enabled", "true",
                    directoryType, directoryName
                ],
                sourceIsAzDo,
                targetIsAzDo,
                trigger);
    }

    public async Task<PullRequest> WaitForPullRequestComment(
        string targetRepo,
        string targetBranch,
        string partialComment,
        int attempts = 40)
    {
        PullRequest pullRequest = await WaitForPullRequestAsync(targetRepo, targetBranch);

        while (attempts-- > 0)
        {
            pullRequest = await GitHubApi.PullRequest.Get(TestParameters.GitHubTestOrg, targetRepo, pullRequest.Number);
            IReadOnlyList<IssueComment> comments = await GitHubApi.Issue.Comment.GetAllForIssue(TestParameters.GitHubTestOrg, targetRepo, pullRequest.Number);
            if (comments.Any(comment => comment.Body.Contains(partialComment)))
            {
                return pullRequest;
            }
            await Task.Delay(5000);
        }

        throw new ScenarioTestException($"Comment containing '{partialComment}' was not found in the pull request.");
    }

    public async Task CheckConflictPullRequestComment(
        string targetRepo,
        string targetBranch,
        PullRequest pullRequest,
        string expectedComment)
    {
        IReadOnlyList<IssueComment> comments = await GitHubApi.Issue.Comment.GetAllForIssue(TestParameters.GitHubTestOrg, targetRepo, pullRequest.Number);
        comments.Should().ContainSingle(comment => comment.Body.Contains(expectedComment));
    }
}
