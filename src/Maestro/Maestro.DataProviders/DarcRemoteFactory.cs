// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Maestro.AzureDevOps;
using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.Logging;

namespace Maestro.DataProviders;

public class DarcRemoteFactory : IRemoteFactory
{
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly OperationManager _operations;
    private readonly BuildAssetRegistryContext _context;
    private readonly DarcRemoteMemoryCache _cache;
    private readonly IGitHubTokenProvider _gitHubTokenProvider;
    private readonly IAzureDevOpsTokenProvider _azureDevOpsTokenProvider;

    public DarcRemoteFactory(
        BuildAssetRegistryContext context,
        IGitHubTokenProvider gitHubTokenProvider,
        IAzureDevOpsTokenProvider azureDevOpsTokenProvider,
        IVersionDetailsParser versionDetailsParser,
        DarcRemoteMemoryCache memoryCache,
        OperationManager operations)
    {
        _operations = operations;
        _versionDetailsParser = versionDetailsParser;

        _context = context;
        _gitHubTokenProvider = gitHubTokenProvider;
        _azureDevOpsTokenProvider = azureDevOpsTokenProvider;
        _cache = memoryCache;
    }

    public async Task<IRemote> GetRemoteAsync(string repoUrl, ILogger logger)
    {
        using (_operations.BeginOperation($"Getting remote for repo {repoUrl}."))
        {
            IRemoteGitRepo remoteGitClient = await GetRemoteGitClient(repoUrl, logger);
            return new Remote(remoteGitClient, _versionDetailsParser, logger);
        }
    }

    public async Task<IDependencyFileManager> GetDependencyFileManagerAsync(string repoUrl, ILogger logger)
    {
        using (_operations.BeginOperation($"Getting remote file manager for repo {repoUrl}."))
        {
            IRemoteGitRepo remoteGitClient = await GetRemoteGitClient(repoUrl, logger);
            return new DependencyFileManager(remoteGitClient, _versionDetailsParser, logger);
        }
    }

    private async Task<IRemoteGitRepo> GetRemoteGitClient(string repoUrl, ILogger logger)
    {
        // Normalize the url with the AzDO client prior to attempting to
        // get a token. When we do coherency updates we build a repo graph and
        // may end up traversing links to classic azdo uris.
        string normalizedUrl = AzureDevOpsClient.NormalizeUrl(repoUrl);

        long installationId = await _context.GetInstallationId(normalizedUrl);
        var repoType = GitRepoUrlParser.ParseTypeFromUri(normalizedUrl);

        if (repoType == GitRepoType.GitHub && installationId == default)
        {
            throw new GithubApplicationInstallationException($"No installation is available for repository '{normalizedUrl}'");
        }

        var remoteConfiguration = repoType switch
        {
            GitRepoType.GitHub => new RemoteConfiguration(
                gitHubToken: await _gitHubTokenProvider.GetTokenForInstallationAsync(installationId)),
            GitRepoType.AzureDevOps => new RemoteConfiguration(
                azureDevOpsToken: await _azureDevOpsTokenProvider.GetTokenForRepository(normalizedUrl)),

            _ => throw new NotImplementedException($"Unknown repo url type {normalizedUrl}"),
        };

        return repoType switch
        {
            GitRepoType.GitHub => installationId == default
                ? throw new GithubApplicationInstallationException($"No installation is available for repository '{normalizedUrl}'")
                : new GitHubClient(
                    gitExecutable: null,
                    remoteConfiguration.GitHubToken,
                    logger,
                    temporaryRepositoryPath: null,
                    _cache.Cache),

            GitRepoType.AzureDevOps => new AzureDevOpsClient(
                gitExecutable: null,
                remoteConfiguration.AzureDevOpsToken,
                logger,
                temporaryRepositoryPath: null),

            _ => throw new NotImplementedException($"Unknown repo url type {normalizedUrl}"),
        };
    }
}
