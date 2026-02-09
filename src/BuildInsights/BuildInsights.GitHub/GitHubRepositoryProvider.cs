// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Octokit;

namespace BuildInsights.GitHub;

public interface IGithubRepositoryService
{
    Task<string> GetFileAsync(string repository, string path, string targetBranch);
}

public class GithubRepositoryProvider : IGithubRepositoryService
{
    private readonly IGitHubApplicationClientFactory _gitHubApplicationClientFactory;
    private readonly ILogger<GithubRepositoryProvider> _logger;

    public GithubRepositoryProvider(IGitHubApplicationClientFactory gitHubApplicationClientFactory,
        ILogger<GithubRepositoryProvider> logger)
    {
        _gitHubApplicationClientFactory = gitHubApplicationClientFactory;
        _logger = logger;
    }

    public async Task<string> GetFileAsync(string repository, string path, string targetBranch)
    {
        if (string.IsNullOrEmpty(repository) || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(targetBranch))
        {
            _logger.LogInformation("Unable to retrieve Build Analysis settings file as repository: {repository}, path: {path} or target branch: {targetBranch} information is missing", repository, path, targetBranch);
            return string.Empty;
        }

        _logger.LogInformation("Getting build analysis settings file for repo {repository} for targetBranch {targetBranch}", repository, targetBranch);

        (string owner, string name) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);
        IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);

        try
        {
            IReadOnlyList<RepositoryContent> buildAnalysisSettingsFile = await client.Repository.Content.GetAllContentsByRef(owner, name, path, targetBranch);
            return buildAnalysisSettingsFile[0].Content;
        }
        catch (NotFoundException)
        {
            _logger.LogInformation("Build Analysis settings file not found for repo {repository} for targetBranch {targetBranch}", repository, targetBranch);
            return string.Empty;
        }

    }
}
