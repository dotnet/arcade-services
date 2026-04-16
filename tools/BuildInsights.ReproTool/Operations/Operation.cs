// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Octokit;
using GitHubClient = Octokit.GitHubClient;

namespace BuildInsights.ReproTool.Operations;

internal abstract class Operation(ILogger logger, GitHubClient ghClient)
{
    protected const string MaestroAuthTestOrgName = "maestro-auth-test";
    protected const string ReproRepoName = "build-result-analysis-test";
    protected const string DefaultTargetBranch = "main";
    protected const string BuildInsightsCheckName = "Build Insights";

    protected ILogger Logger { get; } = logger;
    protected GitHubClient GitHubClient { get; } = ghClient;

    internal abstract Task RunAsync();

    protected async Task<AsyncDisposableValue<string>> CreateTmpBranchAsync(string repoName, string sourceSha, bool skipCleanup)
    {
        string newBranchName = $"repro/{Guid.NewGuid()}";
        Logger.LogInformation("Creating temporary branch {branch} in {org}/{repo} from {sha}",
            newBranchName,
            MaestroAuthTestOrgName,
            repoName,
            sourceSha);

        var newBranch = new NewReference($"refs/heads/{newBranchName}", sourceSha);
        await GitHubClient.Git.Reference.Create(MaestroAuthTestOrgName, repoName, newBranch);

        return AsyncDisposableValue.Create(newBranchName, async branchName =>
        {
            if (skipCleanup)
            {
                return;
            }

            Logger.LogInformation("Cleaning up temporary branch {branch}", branchName);
            try
            {
                await GitHubClient.Git.Reference.Delete(MaestroAuthTestOrgName, repoName, $"heads/{branchName}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to delete temporary branch {branch}", branchName);
            }
        });
    }

    protected async Task CreateOrUpdateFileAsync(
        string owner,
        string repoName,
        string branchName,
        string path,
        string content,
        string commitMessage)
    {
        try
        {
            IReadOnlyList<RepositoryContent> existing = await GitHubClient.Repository.Content.GetAllContentsByRef(owner, repoName, path, branchName);
            RepositoryContent file = existing.First();
            await GitHubClient.Repository.Content.UpdateFile(
                owner,
                repoName,
                path,
                new UpdateFileRequest(commitMessage, content, file.Sha, branchName));
        }
        catch (NotFoundException)
        {
            await GitHubClient.Repository.Content.CreateFile(
                owner,
                repoName,
                path,
                new CreateFileRequest(commitMessage, content, branchName));
        }
    }

    protected async Task<AsyncDisposableValue<PullRequest>> CreateReproPullRequestAsync(
        string branchName,
        string title,
        string body,
        bool skipCleanup)
    {
        Logger.LogInformation("Creating repro pull request from {branch} into {targetBranch}", branchName, DefaultTargetBranch);

        PullRequest pullRequest = await GitHubClient.PullRequest.Create(
            MaestroAuthTestOrgName,
            ReproRepoName,
            new NewPullRequest(title, $"{MaestroAuthTestOrgName}:{branchName}", DefaultTargetBranch)
            {
                Body = body,
            });

        return AsyncDisposableValue.Create(pullRequest, async createdPr =>
        {
            if (skipCleanup)
            {
                return;
            }

            Logger.LogInformation("Closing repro pull request {prNumber}", createdPr.Number);
            try
            {
                await GitHubClient.PullRequest.Update(
                    MaestroAuthTestOrgName,
                    ReproRepoName,
                    createdPr.Number,
                    new PullRequestUpdate { State = ItemState.Closed });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to close repro pull request {prNumber}", createdPr.Number);
            }
        });
    }

    protected async Task<CheckRun?> TryGetBuildInsightsCheckAsync(string owner, string repoName, string sha)
    {
        CheckRunsResponse response = await GitHubClient.Check.Run.GetAllForReference(owner, repoName, sha);
        return response.CheckRuns.FirstOrDefault(c => string.Equals(c.Name, BuildInsightsCheckName, StringComparison.OrdinalIgnoreCase));
    }

    protected async Task<CheckRun?> WaitForBuildInsightsCheckAsync(
        string owner,
        string repoName,
        string sha,
        TimeSpan timeout,
        bool requireCompletion,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CheckRun? check = await TryGetBuildInsightsCheckAsync(owner, repoName, sha);
            if (check != null && (!requireCompletion || check.Status == Octokit.CheckStatus.Completed))
            {
                return check;
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }

        return null;
    }
}
