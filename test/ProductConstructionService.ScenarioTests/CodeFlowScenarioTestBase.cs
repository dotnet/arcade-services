﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using FluentAssertions;

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
        var expectedPRTitle = GetCodeFlowPRName(targetBranch, sourceRepoName);

        Octokit.PullRequest pullRequest = await WaitForPullRequestAsync(targetRepoName, targetBranch);
        IReadOnlyList<Octokit.PullRequestFile> files = await GitHubApi.PullRequest.Files(TestParameters.GitHubTestOrg, targetRepoName, pullRequest.Number);

        try
        {
            files.Count.Should().Be(testFiles.Length + 3);

            // Verify source-manifest has changes
            var sourceManifestFile = files.FirstOrDefault(file => file.FileName == "src/source-manifest.json");
            sourceManifestFile.Should().NotBeNull();

            // Verify git-info
            var allRepoVersionsFile = files.FirstOrDefault(file => file.FileName == "prereqs/git-info/AllRepoVersions.props");
            allRepoVersionsFile.Should().NotBeNull();

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
        finally
        {
            // close the PR
            await GitHubApi.PullRequest.Update(
                TestParameters.GitHubTestOrg,
                targetRepoName,
                pullRequest.Number,
                new Octokit.PullRequestUpdate
                {
                    State = Octokit.ItemState.Closed
                });
        }
    }

    protected async Task CheckBackwardFlowGitHubPullRequest(
        string sourceRepoName,
        string targetRepoName,
        string targetBranch,
        string[] testFiles,
        Dictionary<string, string> testFilePatches,
        string commitSha)
    {
        var expectedPRTitle = GetCodeFlowPRName(targetBranch, sourceRepoName);

        Octokit.PullRequest pullRequest = await WaitForPullRequestAsync(targetRepoName, targetBranch);
        IReadOnlyList<Octokit.PullRequestFile> files = await GitHubApi.PullRequest.Files(TestParameters.GitHubTestOrg, targetRepoName, pullRequest.Number);

        try
        {
            var versionDetailsFile = files.FirstOrDefault(file => file.FileName == "eng/Version.Details.xml");
            versionDetailsFile.Should().NotBeNull();
            versionDetailsFile!.Patch.Should().Contain(GetExpectedCodeFlowDependencyVersionEntry(sourceRepoName, commitSha));

            // Verify new files are in the PR
            foreach (var testFile in testFiles)
            {
                var newFile = files.FirstOrDefault(file => file.FileName == $"{testFile}");
                newFile.Should().NotBeNull();
                newFile!.Patch.Should().Be(testFilePatches[testFile]);
            }
        }
        finally
        {
            // close the PR
            await GitHubApi.PullRequest.Update(
                TestParameters.GitHubTestOrg,
                targetRepoName,
                pullRequest.Number,
                new Octokit.PullRequestUpdate
                {
                    State = Octokit.ItemState.Closed
                });
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
}
