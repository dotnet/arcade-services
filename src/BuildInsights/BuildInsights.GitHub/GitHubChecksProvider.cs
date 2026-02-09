// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.GitHub.Models;
using Maestro.Common;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Octokit;
using CheckRun = BuildInsights.GitHub.Models.CheckRun;

namespace BuildInsights.GitHub;

public interface IGitHubChecksService
{
    Task<long> PostChecksResultAsync(
        string checkRunName,
        string checkRunOutputName,
        string markdown,
        string repository,
        string commitHash,
        CheckResult result,
        CancellationToken cancellationToken);

    Task<bool> IsRepositorySupported(string repository);
    Task<IEnumerable<CheckRun>> GetBuildCheckRunsAsync(string repository, string sha);
    Task<IEnumerable<CheckRun>> GetAllCheckRunsAsync(string repository, string sha);
    Task<CheckRun> GetCheckRunAsyncForApp(string repository, string sha, int appId, string chenRunName);
    Task UpdateCheckRunConclusion(CheckRun checkRun, string repository, string updatedBody, Octokit.CheckConclusion result);
    Task<bool> RepositoryHasIssues(string repository);
    Task<GitHubIssue> GetIssueAsync(string repository, long issueId);

}

public class GitHubChecksProvider : IGitHubChecksService
{
    private readonly IGitHubApplicationClientFactory _gitHubApplicationClientFactory;
    private readonly ILogger<GitHubChecksProvider> _logger;
    private const int AzurePipelinesAppID = 9426;

    public GitHubChecksProvider(
        IGitHubApplicationClientFactory gitHubApplicationClientFactory,
        ILogger<GitHubChecksProvider> logger)
    {
        _gitHubApplicationClientFactory = gitHubApplicationClientFactory;
        _logger = logger;
    }

    public async Task<long> PostChecksResultAsync(
        string checkRunName,
        string checkRunOutputName,
        string markdown,
        string repository,
        string commitHash,
        CheckResult result,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Preparing to send check ({checkLength} bytes) for repo {repository} for commit {commitHash}",
            markdown.Length,
            repository,
            commitHash
        );

        (string name, string owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);
        IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);

        Octokit.CheckStatus checkStatus = result switch
        {
            CheckResult.InProgress => Octokit.CheckStatus.InProgress,
            _ => Octokit.CheckStatus.Completed
        };

        var checkRun = new NewCheckRun(checkRunName, commitHash)
        {
            Output = new NewCheckRunOutput(checkRunOutputName, "")
            {
                Text = markdown
            },
            Status = checkStatus,
            DetailsUrl = "https://github.com/dotnet/arcade/blob/main/Documentation/Projects/Build%20Analysis/LandingPage.md",
            Conclusion = GetConclusion(result)
        };

        Octokit.CheckRun created = await client.Check.Run.Create(owner, name, checkRun);
        _logger.LogInformation("Successfully sent check with id {gitHubCheckId} in suite {gitHubCheckSuiteId}", created.Id, created.CheckSuite?.Id);
        return created.Id;
    }

    private Octokit.CheckConclusion? GetConclusion(CheckResult result) => result switch
    {
        CheckResult.Passed => Octokit.CheckConclusion.Success,
        CheckResult.Failed => Octokit.CheckConclusion.Failure,
        CheckResult.InProgress => null,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
    };

    public async Task<bool> IsRepositorySupported(string repository)
    {
        (string name, string owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);
        try
        {
            IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);
            RepositoriesResponse repos = await client.GitHubApps.Installation.GetAllRepositoriesForCurrent();
            return repos.Repositories.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        catch (NotFoundException)
        {
            _logger.LogInformation($"Not able to find GitHubClient for {owner}/{name}");
            return false;
        }
    }

    public async Task<bool> RepositoryHasIssues(string repository)
    {
        (string name, string owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);
        try
        {
            IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);
            Octokit.Repository repo = await client.Repository.Get(owner, name);
            return repo.HasIssues;
        }
        catch (NotFoundException)
        {
            _logger.LogInformation($"Not able to find GitHubClient for {owner}/{name}");
            return false;
        }
    }

    /// <summary>
    ///     Gets all the GitHub CheckRuns related to the Azure Pipelines (Azure DevOps) GitHub Check Suite, and returns a list
    ///     of the builds that were
    ///     triggered from the pull request
    /// </summary>
    /// <param name="repository">example: dotnet/arcade</param>
    /// <param name="sha">SHA of the Pull Request</param>
    /// <returns></returns>
    public async Task<IEnumerable<Models.CheckRun>> GetBuildCheckRunsAsync(string repository, string sha)
    {
        _logger.LogInformation("Fetching build check run set for commit: {repository}/{commitHash}", repository, sha);
        (string name, string owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);
        IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);

        CheckRunsResponse allCheckRuns = await client.Check.Run.GetAllForReference(owner, name, sha);
        IEnumerable<Octokit.CheckRun> buildCheckRuns =
            allCheckRuns.CheckRuns.Where(x => !string.IsNullOrEmpty(x.ExternalId));

        var filteredRuns = buildCheckRuns.Where(r => r.App.Id == AzurePipelinesAppID);

        IEnumerable<Models.CheckRun> checkRuns = filteredRuns.Select(x => new Models.CheckRun(x));
        HashSet<Models.CheckRun> filteredCheckRuns = checkRuns.ToHashSet(new CheckRunEqualityComparer());
        _logger.LogInformation("Found {count} build check runs", filteredCheckRuns.Count);

        return filteredCheckRuns;
    }

    /// <summary>
    ///     Gets all the GitHub CheckRuns that were triggered from the pull request.
    /// </summary>
    /// <param name="repository">example: dotnet/arcade</param>
    /// <param name="sha">SHA of the Pull Request</param>
    /// <returns></returns>
    public async Task<IEnumerable<Models.CheckRun>> GetAllCheckRunsAsync(string repository, string sha)
    {
        _logger.LogInformation("Fetching full check run set for commit: {repository}/{commitHash}", repository, sha);
        (string name, string owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);
        IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);

        CheckRunsResponse allCheckRuns = await client.Check.Run.GetAllForReference(owner, name, sha);
        _logger.LogInformation("Found {count} check runs", allCheckRuns.CheckRuns.Count);
        return allCheckRuns.CheckRuns.Select(x => new Models.CheckRun(x));
    }

    public async Task<Models.CheckRun> GetCheckRunAsyncForApp(string repository, string sha, int appId, string chenRunName)
    {
        _logger.LogInformation("Fetching build check run set for commit: {repository}/{commitHash}", repository, sha);
        (string name, string owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);
        IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);

        CheckRunsResponse allCheckRuns = await client.Check.Run.GetAllForReference(owner, name, sha);
        IEnumerable<Octokit.CheckRun> filteredRuns = allCheckRuns.CheckRuns.Where(r => r.App.Id == appId);
        IEnumerable<Models.CheckRun> checkRuns = filteredRuns.Select(x => new Models.CheckRun(x));

        return checkRuns.FirstOrDefault(c => c.Name.Equals(chenRunName, StringComparison.CurrentCultureIgnoreCase));
    }

    public async Task<GitHubIssue> GetIssueAsync(string repository, long issueId)
    {
        (string name, string owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);
        IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);
        Issue issue = await client.Issue.Get(owner, name, Convert.ToInt32(issueId));
        return new GitHubIssue(
            id: issue.Number,
            title: issue.Title,
            repositoryWithOwner: repository,
            body: issue.Body,
            linkGitHubIssue: issue.HtmlUrl,
            labels: issue.Labels.Select(l => l.Name).ToList());
    }

    public async Task UpdateCheckRunConclusion(Models.CheckRun checkRun, string repository, string updatedBody, Octokit.CheckConclusion result)
    {
        (string name, string owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);

        var checkRunUpdate = new CheckRunUpdate
        {
            Status = Octokit.CheckStatus.Completed,
            Conclusion = result,
            Output = new NewCheckRunOutput(checkRun.Title, checkRun.Summary) { Text = updatedBody }
        };

        try
        {
            _logger.LogInformation("Starting check run updated successfully.");
            IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);
            await client.Check.Run.Update(owner, name, checkRun.CheckRunId, checkRunUpdate);
            _logger.LogInformation("Check run updated successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating check run: {checkRun}", checkRun.CheckRunId);
            throw;
        }
    }
}
