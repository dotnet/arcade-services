// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using DarcGitHubClient = Microsoft.DotNet.DarcLib.GitHubClient;

namespace ProductConstructionService.DependencyFlow;

internal interface IPullRequestApprover
{
    Task ApprovePullRequestAsync(string pullRequestUrl, string reviewBody, CancellationToken cancellationToken = default);
}

/// <summary>
/// Approves GitHub codeflow pull requests using a dedicated GitHub App that has permission
/// to approve pull requests.
/// </summary>
internal class GitHubPullRequestApprover : IPullRequestApprover
{
    /// <summary>
    /// Logical name of the named <see cref="GitHubTokenProviderOptions"/> that holds the approval app's credentials.
    /// </summary>
    internal const string GitHubAppOptionsName = "CodeflowApproval";

    private readonly IGitHubAppTokenProvider _appTokenProvider;
    private readonly IOptions<GitHubClientOptions> _gitHubClientOptions;
    private readonly ILogger<GitHubPullRequestApprover> _logger;

    public GitHubPullRequestApprover(
        IGitHubAppTokenProvider appTokenProvider,
        IOptions<GitHubClientOptions> gitHubClientOptions,
        ILogger<GitHubPullRequestApprover> logger)
    {
        _appTokenProvider = appTokenProvider;
        _gitHubClientOptions = gitHubClientOptions;
        _logger = logger;
    }

    public async Task ApprovePullRequestAsync(string pullRequestUrl, string reviewBody, CancellationToken cancellationToken = default)
    {

        var repoType = GitRepoUrlUtils.ParseTypeFromUri(pullRequestUrl);
        if (repoType != GitRepoType.GitHub)
        {
            _logger.LogInformation(
                "Skipping approval of '{url}'; only GitHub pull requests can be approved by the approval app",
                pullRequestUrl);
            return;
        }

        var (owner, repo, prNumber) = DarcGitHubClient.ParsePullRequestUri(pullRequestUrl);
        if (owner == null || repo == null || prNumber == 0)
        {
            _logger.LogInformation(
                "Skipping approval of '{url}'; only GitHub pull requests can be approved by the approval app",
                pullRequestUrl);
            return;
        }

        GitHubClient installationClient = await CreateInstallationClientAsync(owner, repo);

        await installationClient.PullRequest.Review.Create(
            owner,
            repo,
            prNumber,
            new PullRequestReviewCreate
            {
                Body = reviewBody,
                Event = PullRequestReviewEvent.Approve
            });

        _logger.LogInformation("Approved pull request {url} using the approval GitHub App", pullRequestUrl);
    }

    private async Task<GitHubClient> CreateInstallationClientAsync(string owner, string repo)
    {
        GitHubClient appClient = CreateAppClient();
        Installation installation = await appClient.GitHubApps.GetRepositoryInstallationForCurrent(owner, repo);
        AccessToken accessToken = await appClient.GitHubApps.CreateInstallationToken(installation.Id);
        var installationToken = accessToken.Token;

        return new GitHubClient(_gitHubClientOptions.Value.ProductHeader)
        {
            Credentials = new Credentials(installationToken)
        };
    }

    private GitHubClient CreateAppClient()
    {
        var jwt = _appTokenProvider.GetAppToken(GitHubAppOptionsName);
        return new GitHubClient(_gitHubClientOptions.Value.ProductHeader)
        {
            Credentials = new Credentials(jwt, AuthenticationType.Bearer)
        };
    }
}
