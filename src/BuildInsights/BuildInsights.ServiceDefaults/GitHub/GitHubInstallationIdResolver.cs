// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.GitHub.Authentication;

namespace BuildInsights.ServiceDefaults.GitHub;

internal class GitHubInstallationIdResolver : IInstallationLookup
{
    // TODO
    //private readonly IGitHubTokenProvider _gitHubTokenProvider;
    //private readonly ILogger<GitHubInstallationIdResolver> _logger;

    //public GitHubInstallationIdResolver(
    //    IGitHubTokenProvider gitHubTokenProvider,
    //    ILogger<GitHubInstallationIdResolver> logger)
    //{
    //    _gitHubTokenProvider = gitHubTokenProvider;
    //    _logger = logger;
    //}

    public Task<long> GetInstallationId(string repoUri)
    {
        return Task.FromResult(0L);
        //_logger.LogInformation("Getting installation ID for {repoUri}", repoUri);

        //var (repo, owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repoUri);
        //var token = _gitHubTokenProvider.GetTokenForApp();
        //var client = new GitHubClient(new ProductHeaderValue(nameof(ProductConstructionService)))
        //{
        //    Credentials = new Credentials(token, AuthenticationType.Bearer)
        //};

        //try
        //{
        //    var installation = await client.GitHubApps.GetRepositoryInstallationForCurrent(owner, repo);

        //    if (installation == null)
        //    {
        //        _logger.LogInformation("Failed to get installation id for {owner}/{repo}", owner, repo);
        //        return 0;
        //    }

        //    _logger.LogInformation("Installation id for {owner}/{repo} is {installationId}", owner, repo, installation.Id);
        //    return installation.Id;
        //}
        //catch (ApiException e)
        //{
        //    _logger.LogInformation("Failed to get installation id for {owner}/{repo} - {statusCode}.", owner, repo, e.StatusCode);
        //    return 0;
        //}
    }

    // TODO?
    public Task<bool> IsOrganizationSupported(string org) => Task.FromResult(true);
}
