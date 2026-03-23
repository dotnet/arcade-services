// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Maestro.Common.Cache;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Octokit;

namespace BuildInsights.ServiceDefaults.GitHub;

internal class GitHubInstallationIdResolver : IInstallationLookup
{
    private const string CacheKeyPrefix = "github-installation-id";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(30);

    private readonly IGitHubAppTokenProvider _gitHubTokenProvider;
    private readonly IRedisCacheFactory _cacheFactory;
    private readonly ILogger<GitHubInstallationIdResolver> _logger;

    public GitHubInstallationIdResolver(
        IGitHubAppTokenProvider gitHubTokenProvider,
        IRedisCacheFactory cacheFactory,
        ILogger<GitHubInstallationIdResolver> logger)
    {
        _gitHubTokenProvider = gitHubTokenProvider;
        _cacheFactory = cacheFactory;
        _logger = logger;
    }

    public async Task<long> GetInstallationId(string repoUri)
    {
        var (repo, owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repoUri);
        var cacheKey = $"{CacheKeyPrefix}:{owner}/{repo}";
        var cache = _cacheFactory.Create(cacheKey);

        var cachedValue = await cache.TryGetAsync();
        if (cachedValue is not null && long.TryParse(cachedValue, out var cachedInstallationId))
        {
            _logger.LogDebug("Found cached installation ID for {owner}/{repo}", owner, repo);
            return cachedInstallationId;
        }

        _logger.LogInformation("Fetching installation ID for {owner}/{repo} from GitHub", owner, repo);

        var token = _gitHubTokenProvider.GetAppToken();
        var client = new GitHubClient(new ProductHeaderValue(nameof(ProductConstructionService)))
        {
            Credentials = new Credentials(token, AuthenticationType.Bearer)
        };

        Installation installation = await client.GitHubApps.GetRepositoryInstallationForCurrent(owner, repo)
            ?? throw new Exception(string.Format("Failed to get installation id for {0}/{1}", owner, repo));

        await cache.SetAsync(installation.Id.ToString(), CacheExpiration);

        _logger.LogInformation("Cached installation ID {installationId} for {owner}/{repo}", installation.Id, owner, repo);
        return installation.Id;
    }

    // TODO?
    public Task<bool> IsOrganizationSupported(string org) => Task.FromResult(true);
}
