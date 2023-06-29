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
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SubscriptionActorService;

public class DarcRemoteFactory : IRemoteFactory
{
    public DarcRemoteFactory(
        IConfiguration configuration,
        IGitHubTokenProvider gitHubTokenProvider,
        IAzureDevOpsTokenProvider azureDevOpsTokenProvider,
        DarcRemoteMemoryCache memoryCache,
        BuildAssetRegistryContext context,
        TemporaryFiles tempFiles,
        ILocalGit localGit,
        IVersionDetailsParser versionDetailsParser,
        OperationManager operations)
    {
        _tempFiles = tempFiles;
        _localGit = localGit;
        _versionDetailsParser = versionDetailsParser;
        _operations = operations;
        _configuration = configuration;
        _gitHubTokenProvider = gitHubTokenProvider;
        _azureDevOpsTokenProvider = azureDevOpsTokenProvider;
        _cache = memoryCache;
        _context = context;
    }

    private readonly IConfiguration _configuration;
    private readonly IGitHubTokenProvider _gitHubTokenProvider;
    private readonly IAzureDevOpsTokenProvider _azureDevOpsTokenProvider;
    private readonly BuildAssetRegistryContext _context;
    private readonly DarcRemoteMemoryCache _cache;

    private readonly TemporaryFiles _tempFiles;
    private readonly ILocalGit _localGit;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly OperationManager _operations;

    public Task<IRemote> GetBarOnlyRemoteAsync(ILogger logger)
    {
        return Task.FromResult((IRemote)new Remote(null, new MaestroBarClient(_context), _versionDetailsParser, logger));
    }

    public async Task<IRemote> GetRemoteAsync(string repoUrl, ILogger logger)
    {
        using (_operations.BeginOperation($"Getting remote for repo {repoUrl}."))
        {
            // Normalize the url with the AzDO client prior to attempting to
            // get a token. When we do coherency updates we build a repo graph and
            // may end up traversing links to classic azdo uris.
            string normalizedUrl = AzureDevOpsClient.NormalizeUrl(repoUrl);
            Uri normalizedRepoUri = new Uri(normalizedUrl);
            // Look up the setting for where the repo root should be held.  Default to empty,
            // which will use the temp directory.
            string temporaryRepositoryRoot = _configuration.GetValue<string>("DarcTemporaryRepoRoot", null);
            if (string.IsNullOrEmpty(temporaryRepositoryRoot))
            {
                temporaryRepositoryRoot = _tempFiles.GetFilePath("repos");
            }

            long installationId = await _context.GetInstallationId(normalizedUrl);
            var gitExe = _localGit.GetPathToLocalGit();

            IRemoteGitRepo gitClient = GitRepoTypeParser.ParseFromUri(normalizedUrl) switch
            {
                GitRepoType.GitHub => installationId == default
                    ? throw new GithubApplicationInstallationException($"No installation is available for repository '{normalizedUrl}'")
                    : new GitHubClient(
                        gitExe,
                        await _gitHubTokenProvider.GetTokenForInstallationAsync(installationId),
                        logger,
                        temporaryRepositoryRoot,
                        _cache.Cache),

                GitRepoType.AzureDevOps => new AzureDevOpsClient(
                    gitExe,
                    await _azureDevOpsTokenProvider.GetTokenForRepository(normalizedUrl),
                    logger,
                    temporaryRepositoryRoot),

                _ => throw new NotImplementedException($"Unknown repo url type {normalizedUrl}"),
            };

            return new Remote(gitClient, new MaestroBarClient(_context), _versionDetailsParser, logger);
        }
    }
}
