// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SubscriptionActorService;

public class DarcRemoteFactory : IRemoteFactory
{
    private readonly IConfiguration _configuration;
    private readonly IGitHubTokenProvider _gitHubTokenProvider;
    private readonly IAzureDevOpsTokenProvider _azureDevOpsTokenProvider;
    private readonly BuildAssetRegistryContext _context;
    private readonly DarcRemoteMemoryCache _cache;
    private readonly TemporaryFiles _tempFiles;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IProcessManager _processManager;
    private readonly OperationManager _operations;

    public DarcRemoteFactory(
        IConfiguration configuration,
        IGitHubTokenProvider gitHubTokenProvider,
        IAzureDevOpsTokenProvider azureDevOpsTokenProvider,
        DarcRemoteMemoryCache memoryCache,
        BuildAssetRegistryContext context,
        TemporaryFiles tempFiles,
        IVersionDetailsParser versionDetailsParser,
        IProcessManager processManager,
        OperationManager operations)
    {
        _tempFiles = tempFiles;
        _versionDetailsParser = versionDetailsParser;
        _processManager = processManager;
        _operations = operations;
        _configuration = configuration;
        _gitHubTokenProvider = gitHubTokenProvider;
        _azureDevOpsTokenProvider = azureDevOpsTokenProvider;
        _cache = memoryCache;
        _context = context;
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

        // Look up the setting for where the repo root should be held.  Default to empty,
        // which will use the temp directory.
        string temporaryRepositoryRoot = _configuration.GetValue<string>("DarcTemporaryRepoRoot", null);
        if (string.IsNullOrEmpty(temporaryRepositoryRoot))
        {
            temporaryRepositoryRoot = _tempFiles.GetFilePath("repos");
        }

        long installationId = await _context.GetInstallationId(normalizedUrl);
        var repoType = GitRepoUrlParser.ParseTypeFromUri(normalizedUrl);

        if (repoType == GitRepoType.GitHub && installationId == default)
        {
            throw new GithubApplicationInstallationException($"No installation is available for repository '{normalizedUrl}'");
        }

        return GitRepoUrlParser.ParseTypeFromUri(normalizedUrl) switch
        {
            GitRepoType.GitHub => installationId == default
                ? throw new GithubApplicationInstallationException($"No installation is available for repository '{normalizedUrl}'")
                : new GitHubClient(
                    new ResolvedTokenProvider(await _gitHubTokenProvider.GetTokenForInstallationAsync(installationId)),
                    _processManager,
                    logger,
                    temporaryRepositoryRoot,
                    _cache.Cache),

            GitRepoType.AzureDevOps => new AzureDevOpsClient(
                _azureDevOpsTokenProvider,
                _processManager,
                logger,
                temporaryRepositoryRoot),

            _ => throw new NotImplementedException($"Unknown repo url type {normalizedUrl}"),
        };
    }
}
