// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using DarcGitHubClient = Microsoft.DotNet.DarcLib.GitHubClient;

namespace Microsoft.DotNet.DarcLib;

public interface IPullRequestApprover
{
    Task ApprovePullRequestAsync(
        string pullRequestUrl,
        string approvedCommitSha,
        string reviewBody,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Approves GitHub codeflow pull requests using a dedicated GitHub App that has permission
/// to approve pull requests.
/// </summary>
public class GitHubPullRequestApprover : IPullRequestApprover
{
    /// <summary>
    /// Logical name of the named <see cref="GitHubTokenProviderOptions"/> that holds the approval app's credentials.
    /// </summary>
    public const string GitHubAppOptionsName = "CodeflowApproval";

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

    public async Task ApprovePullRequestAsync(
        string pullRequestUrl,
        string approvedCommitSha,
        string reviewBody,
        CancellationToken cancellationToken = default)
    {

        var repoType = GitRepoUrlUtils.ParseTypeFromUri(pullRequestUrl);
        if (repoType != GitRepoType.GitHub)
        {
            throw new InvalidOperationException($"Can not approve non GitHub pull request: {pullRequestUrl}");
        }

        var (owner, repo, prNumber) = DarcGitHubClient.ParsePullRequestUri(pullRequestUrl);
        if (owner == null || repo == null || prNumber == 0)
        {
            throw new InvalidOperationException($"Unable to parse GitHub pull request Uri {pullRequestUrl}");
        }

        Octokit.GitHubClient installationClient = await CreateInstallationClientAsync(owner, repo);

        await installationClient.PullRequest.Review.Create(
            owner,
            repo,
            prNumber,
            new PullRequestReviewCreate
            {
                CommitId = approvedCommitSha,
                Body = reviewBody,
                Event = PullRequestReviewEvent.Approve
            });

        _logger.LogInformation("Approved pull request {url} using the approval GitHub App", pullRequestUrl);
    }

    private async Task<Octokit.GitHubClient> CreateInstallationClientAsync(string owner, string repo)
    {
        Octokit.GitHubClient appClient = CreateAppClient();
        Installation installation = await appClient.GitHubApps.GetRepositoryInstallationForCurrent(owner, repo);
        AccessToken accessToken = await appClient.GitHubApps.CreateInstallationToken(installation.Id);
        var installationToken = accessToken.Token;

        return new Octokit.GitHubClient(_gitHubClientOptions.Value.ProductHeader)
        {
            Credentials = new Credentials(installationToken)
        };
    }

    private Octokit.GitHubClient CreateAppClient()
    {
        var jwt = _appTokenProvider.GetAppToken(GitHubAppOptionsName);
        return new Octokit.GitHubClient(_gitHubClientOptions.Value.ProductHeader)
        {
            Credentials = new Credentials(jwt, AuthenticationType.Bearer)
        };
    }
}
