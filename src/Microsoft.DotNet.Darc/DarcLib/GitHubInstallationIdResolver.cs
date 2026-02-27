// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Octokit;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface IGitHubInstallationIdResolver
{
    Task<long?> GetInstallationIdForRepository(string repoUri);
}

public class GitHubInstallationIdResolver : IGitHubInstallationIdResolver
{
    private readonly IGitHubAppTokenProvider _gitHubTokenProvider;
    private readonly ILogger<GitHubInstallationIdResolver> _logger;

    public GitHubInstallationIdResolver(
        IGitHubAppTokenProvider gitHubTokenProvider,
        ILogger<GitHubInstallationIdResolver> logger)
    {
        _gitHubTokenProvider = gitHubTokenProvider;
        _logger = logger;
    }

    public async Task<long?> GetInstallationIdForRepository(string repoUri)
    {
        _logger.LogInformation("Getting installation ID for {repoUri}", repoUri);

        var (owner, repo) = GitHubClient.ParseRepoUri(repoUri);
        var token = _gitHubTokenProvider.GetAppToken();
        var client = new Octokit.GitHubClient(new ProductHeaderValue(nameof(ProductConstructionService)))
        {
            Credentials = new Credentials(token, AuthenticationType.Bearer)
        };

        try
        {
            var installation = await client.GitHubApps.GetRepositoryInstallationForCurrent(owner, repo);

            if (installation == null)
            {
                _logger.LogInformation("Failed to get installation id for {owner}/{repo}", owner, repo);
                return null;
            }

            _logger.LogInformation("Installation id for {owner}/{repo} is {installationId}", owner, repo, installation.Id);
            return installation.Id;
        }
        catch (ApiException e)
        {
            _logger.LogInformation("Failed to get installation id for {owner}/{repo} - {statusCode}.", owner, repo, e.StatusCode);
            return null;
        }
    }
}
