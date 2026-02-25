// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub;
using Maestro.Common;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Octokit;

namespace BuildInsights.BuildAnalysis;

public interface IAzDoToGitHubRepositoryService
{
    Task<AzDoToGitHubRepositoryResult> TryGetGitHubRepositorySupportingKnownIssues(BuildRepository buildRepository, string commit);
    Task<bool> IsInternalRepositorySupported(BuildRepository repository, string commit);
}

public class AzDoToGitHubRepositoryProvider : IAzDoToGitHubRepositoryService
{
    private readonly IGitHubApplicationClientFactory _gitHubApplicationClientFactory;
    private readonly IGitHubChecksService _gitHubChecksService;

    private readonly ILogger<AzDoToGitHubRepositoryProvider> _logger;

    public AzDoToGitHubRepositoryProvider(
        IGitHubChecksService gitHubChecksService,
        IGitHubApplicationClientFactory gitHubApplicationClientFactory,
        ILogger<AzDoToGitHubRepositoryProvider> logger)
    {
        _gitHubApplicationClientFactory = gitHubApplicationClientFactory;
        _gitHubChecksService = gitHubChecksService;
        _logger = logger;
    }

    public async Task<bool> IsInternalRepositorySupported(BuildRepository repository, string commit)
    {
        string repositorySupported = await TryGetGitHubSupportedRepository(repository, commit);
        return !string.IsNullOrEmpty(repositorySupported);
    }

    public async Task<AzDoToGitHubRepositoryResult> TryGetGitHubRepositorySupportingKnownIssues(BuildRepository repository, string commit)
    {
        string supportedRepository = await TryGetGitHubSupportedRepository(repository, commit);
        if (!string.IsNullOrEmpty(supportedRepository))
        {
            if (await _gitHubChecksService.IsRepositorySupported(supportedRepository) &&
                await _gitHubChecksService.RepositoryHasIssues(supportedRepository))
            {
                return new AzDoToGitHubRepositoryResult(true, supportedRepository);
            }

            _logger.LogInformation("The repository {repository} is not supported by build analysis or it doesn't have issues", supportedRepository);
            return new AzDoToGitHubRepositoryResult(false);
        }

        return new AzDoToGitHubRepositoryResult(false);
    }

    private async Task<string> TryGetGitHubSupportedRepository(BuildRepository repository, string commit)
    {
        if (!IsRepositoryTypeSupported(repository.Type))
        {
            return string.Empty;
        }

        string repoIdentity = GetGithubRepoName(repository.Name);
        try
        {
            (string name, string owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repoIdentity);
            IGitHubClient githubClient = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);
            await githubClient.Repository.Get(owner, name);
            return repoIdentity;
        }
        catch (NotFoundException)
        {
            _logger.LogInformation(
                "Unable to translate AzDO repository to GitHub repository for repoIdentity: {repoIdentity} and commit: {commit}.",
repoIdentity, commit);

            return string.Empty;
        }
        catch (System.ArgumentException)
        {
            _logger.LogInformation(
                "Unable to translate AzDO repository to GitHub repository name format for repoIdentity: {repoIdentity} and commit: {commit}.",
                repoIdentity, commit);

            return string.Empty;
        }
    }

    private bool IsRepositoryTypeSupported(BuildRepositoryType repositoryType)
    {
        if (repositoryType == BuildRepositoryType.TfsGit)
        {
            return true;
        }

        string notSupportedReason;
        switch (repositoryType)
        {
            case BuildRepositoryType.Git:
            case BuildRepositoryType.GitHub:
                notSupportedReason = "private repositories are not supported.";
                break;
            case BuildRepositoryType.TfsVersionControl:
                notSupportedReason = "dnceng/internal uses Git for version control.";
                break;
            case BuildRepositoryType.Unknown:
            default:
                notSupportedReason = "the repository type was not recognized.";
                break;
        }

        _logger.LogInformation("The repository type {repositoryType} is not supported because {reason} ",
            repositoryType.ToString(), notSupportedReason);

        return false;
    }

    private static string GetGithubRepoName(string repoName)
    {
        int index = repoName.IndexOf('-');
        string repositoryName = index > -1 ? repoName[..index] + "/" + repoName[(index + 1)..] : repoName;

        return repositoryName;
    }
}

public record AzDoToGitHubRepositoryResult(bool IsValidRepositoryAvailable, string? GitHubRepository = null)
{
    public string? GitHubRepository = GitHubRepository;
    public bool IsValidRepositoryAvailable = IsValidRepositoryAvailable;
}
