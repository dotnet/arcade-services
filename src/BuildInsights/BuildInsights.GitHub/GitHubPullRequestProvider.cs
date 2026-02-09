// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Octokit;

namespace BuildInsights.GitHub;

public interface IGitHubPullRequestService
{
    Task<PullRequest> GetPullRequest(string repository, int number);
}

public class GitHubPullRequestProvider : IGitHubPullRequestService
{
    private readonly IGitHubApplicationClientFactory _gitHubApplicationClientFactory;
    private readonly ILogger<GitHubChecksProvider> _logger;

    public GitHubPullRequestProvider(
        IGitHubApplicationClientFactory gitHubApplicationClientFactory,
        ILogger<GitHubChecksProvider> logger)
    {
        _gitHubApplicationClientFactory = gitHubApplicationClientFactory;
        _logger = logger;
    }

    public async Task<PullRequest> GetPullRequest(string repository, int number)
    {
        _logger.LogInformation($"Preparing to get pull request #{number} from {repository}");

        (string owner, string name) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);
        IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);

        PullRequest pullRequest = await client.PullRequest.Get(owner, name, number);
        _logger.LogInformation($"Successfully retrieved pull request #{number} from {repository}");
        return pullRequest;
    }
}
