// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using FluentAssertions;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Octokit;

#nullable enable
namespace ProductConstructionService.ScenarioTests;

internal class CodeFlowScenarioTestBase : ScenarioTestBase
{
    protected async Task CheckForwardFlowGitHubPullRequest(
        string[] sourceRepoNames,
        string targetRepoName,
        string targetBranch,
        string[] testFiles,
        Dictionary<string, string> testFilePatches)
    {
        PullRequest pullRequest = sourceRepoNames.Length > 1
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
                + 1 // source-manifest.json
                + sourceRepoNames.Length); // 1 git-info file per repo

            // Verify source-manifest has changes
            files.Should().Contain(file => file.FileName == VmrInfo.DefaultRelativeSourceManifestPath);

            foreach (var repoName in sourceRepoNames)
            {
                files.Should().Contain(file => file.FileName == $"{VmrInfo.GitInfoSourcesDir}/{repoName}.props");
            }

            // Verify new files are in the PR
            foreach (var testFile in testFiles)
            {
                var newFile = files.FirstOrDefault(file => file.FileName == testFile);
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
        string targetDirectory,
        bool batchable = false)
            => await CreateSourceEnabledSubscriptionAsync(
                sourceChannelName,
                sourceRepo,
                targetRepo,
                targetBranch,
                updateFrequency,
                sourceOrg,
                targetDirectory: targetDirectory,
                batchable: batchable);

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
        string? targetDirectory = null,
        bool batchable = false)
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
            directoryType, directoryName,
        ];

        if (batchable)
        {
            additionalOptions.Add("--batchable");
        }

        return await CreateSubscriptionAsync(
                sourceChannelName,
                sourceRepo,
                targetRepo,
                targetBranch,
                updateFrequency,
                sourceOrg,
                additionalOptions,
                sourceIsAzDo,
                targetIsAzDo,
                trigger);
    }
}
