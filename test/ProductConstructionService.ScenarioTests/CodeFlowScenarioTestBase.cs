// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Octokit;
using ProductConstructionService.ScenarioTests.Helpers;

#nullable enable
namespace ProductConstructionService.ScenarioTests;

internal class CodeFlowScenarioTestBase : ScenarioTestBase
{
    protected async Task<PullRequest> CheckForwardFlowGitHubPullRequest(
        (string Repo, string Commit)[] repoUpdates,
        string targetRepoName,
        string targetBranch,
        string[] testFiles,
        Dictionary<string, string> testFilePatches)
    {
        // When we expect updates from multiple repos (batchable subscriptions), we need to wait until the PR gets updated with the second repository after it is created
        // Otherwise it might try to validate the contents before all updates are in place
        Octokit.PullRequest pullRequest = repoUpdates.Length > 1
            ? await WaitForUpdatedPullRequestAsync(targetRepoName, targetBranch)
            : await WaitForPullRequestAsync(targetRepoName, targetBranch);

        await using (CleanUpPullRequestAfter(TestParameters.GitHubTestOrg, targetRepoName, pullRequest))
        {
            IReadOnlyList<PullRequestFile> files = await GitHubApi.PullRequest.Files(
                TestParameters.GitHubTestOrg,
                targetRepoName,
                pullRequest.Number);

            files.Count.Should().Be(
                testFiles.Length
                + 34); // source-manifest.json and eng/common changes

            // Verify source-manifest has changes
            files.Should().Contain(file => file.FileName == VmrInfo.DefaultRelativeSourceManifestPath);

            // Verify new files are in the PR
            foreach (var testFile in testFiles)
            {
                var newFile = files.FirstOrDefault(file => file.FileName == testFile);
                newFile.Should().NotBeNull();
                newFile!.Patch.Should().Be(testFilePatches[testFile]);
            }

            // Verify the source manifest contains the right versions
            var fileContents = await GitHubApi.Repository.Content.GetAllContentsByRef(
                TestParameters.GitHubTestOrg,
                targetRepoName,
                VmrInfo.DefaultRelativeSourceManifestPath,
                pullRequest.Head.Sha);
            var sourceManifest = SourceManifest.FromJson(fileContents[0].Content);
            foreach (var update in repoUpdates)
            {
                var manifestRecord = sourceManifest.GetRepoVersion(update.Repo);
                manifestRecord.CommitSha.Should().Be(update.Commit);
            }
        }

        return pullRequest;
    }

    protected async Task CheckBackwardFlowGitHubPullRequest(
        string sourceRepoName,
        string targetRepoName,
        string targetBranch,
        string[] testFiles,
        Dictionary<string, string> testFilePatches,
        IReadOnlyList<DependencyDetail> dependenciesToVerify,
        string commitSha,
        int buildId)
    {
        Octokit.PullRequest pullRequest = await WaitForPullRequestAsync(targetRepoName, targetBranch);

        await using (CleanUpPullRequestAfter(TestParameters.GitHubTestOrg, targetRepoName, pullRequest))
        {
            IReadOnlyList<PullRequestFile> files = await GitHubApi.PullRequest.Files(TestParameters.GitHubTestOrg, targetRepoName, pullRequest.Number);

            var versionDetailsFile = files.FirstOrDefault(file => file.FileName == "eng/Version.Details.xml");
            versionDetailsFile.Should().NotBeNull();
            versionDetailsFile!.Patch.Should().Contain(GetExpectedCodeFlowDependencyVersionEntry(sourceRepoName, targetRepoName, commitSha, buildId));

            // Verify new files are in the PR
            foreach (var testFile in testFiles)
            {
                var newFile = files.FirstOrDefault(file => file.FileName == $"{testFile}");
                newFile.Should().NotBeNull();
                newFile!.Patch.Should().Be(testFilePatches[testFile]);
            }

            // Verify eng/common didn't get updated because the Arcade.Sdk is pinned
            files.FirstOrDefault(f => f.FileName.Contains("eng/common")).Should().BeNull();

            // Verify that the dependencies have the expected versions
            IReadOnlyList<RepositoryContent> repositoryContents = await GitHubApi.Repository.Content.GetAllContentsByRef(
                TestParameters.GitHubTestOrg,
                targetRepoName,
                VersionFiles.VersionDetailsXml,
                pullRequest.Head.Ref);
            VersionDetails versionDetails = new VersionDetailsParser().ParseVersionDetailsXml(repositoryContents[0].Content, includePinned: true);
            dependenciesToVerify.All(
                dep => versionDetails.Dependencies.Any(d => d.Name == dep.Name && d.Version == dep.Version)).Should().BeTrue();
        }
    }

    protected async Task<string> CreateForwardFlowSubscriptionAsync(
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

    protected async Task<string> CreateBackwardFlowSubscriptionAsync(
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

    private async Task<string> CreateSourceEnabledSubscriptionAsync(
        string sourceChannelName,
        string sourceRepo,
        string targetRepo,
        string targetBranch,
        string updateFrequency,
        string sourceOrg = "dotnet",
        bool sourceIsAzDo = false,
        bool targetIsAzDo = false,
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

        List<string> additionalOptions =
        [
            "--source-enabled", "true",
            "--standard-automerge",
            directoryType, directoryName,
        ];

        return await CreateSubscriptionAsync(
                sourceChannelName,
                sourceRepo,
                targetRepo,
                targetBranch,
                updateFrequency,
                sourceOrg,
                additionalOptions,
                sourceIsAzDo,
                targetIsAzDo);
    }

    public async Task<Octokit.PullRequest> WaitForPullRequestComment(
        string targetRepo,
        string targetBranch,
        string partialComment,
        int attempts = 40)
    {
        Octokit.PullRequest pullRequest = await WaitForPullRequestAsync(targetRepo, targetBranch);

        while (attempts-- > 0)
        {
            pullRequest = await GitHubApi.PullRequest.Get(TestParameters.GitHubTestOrg, targetRepo, pullRequest.Number);
            IReadOnlyList<IssueComment> comments = await GitHubApi.Issue.Comment.GetAllForIssue(TestParameters.GitHubTestOrg, targetRepo, pullRequest.Number);
            if (comments.Any(comment => comment.Body.Contains(partialComment)))
            {
                return pullRequest;
            }
            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        throw new ScenarioTestException($"Comment containing '{partialComment}' was not found in the pull request {pullRequest.HtmlUrl}.");
    }

    public static async Task CheckIfPullRequestCommentExists(
        string targetRepo,
        Octokit.PullRequest pullRequest,
        string[] stringsExpectedInComment)
    {
        IReadOnlyList<IssueComment> comments = await GitHubApi.Issue.Comment.GetAllForIssue(TestParameters.GitHubTestOrg, targetRepo, pullRequest.Number);

        var allCommentBodies = string.Join(Environment.NewLine, comments.Select(c => c.Body));

        foreach (var expected in stringsExpectedInComment)
        {
            allCommentBodies.Should().Contain(
                expected,
                $"PR {pullRequest.HtmlUrl} should contain '{string.Join("', '", stringsExpectedInComment)}'");
        }
    }
}
