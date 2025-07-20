// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Maestro.DataProviders;

public class RemoteFactory : IRemoteFactory
{
    private readonly OperationManager _operations;
    private readonly IProcessManager _processManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly BuildAssetRegistryContext _context;
    private readonly DarcRemoteMemoryCache _cache;
    private readonly IGitHubTokenProvider _gitHubTokenProvider;
    private readonly IAzureDevOpsTokenProvider _azdoTokenProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRedisCacheClient _redisCacheClient;

    public RemoteFactory(
        BuildAssetRegistryContext context,
        IGitHubTokenProvider gitHubTokenProvider,
        IAzureDevOpsTokenProvider azdoTokenProvider,
        IVersionDetailsParser versionDetailsParser,
        DarcRemoteMemoryCache memoryCache,
        OperationManager operations,
        IProcessManager processManager,
        IRedisCacheClient redisCacheClient,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider)
    {
        _operations = operations;
        _processManager = processManager;
        _loggerFactory = loggerFactory;
        _context = context;
        _gitHubTokenProvider = gitHubTokenProvider;
        _azdoTokenProvider = azdoTokenProvider;
        _cache = memoryCache;
        _serviceProvider = serviceProvider;
        _redisCacheClient = redisCacheClient;
    }

    public async Task<IRemote> CreateRemoteAsync(string repoUrl)
    {
        using (_operations.BeginOperation($"Getting remote for repo {repoUrl}."))
        {
            IRemoteGitRepo remoteGitClient = await GetRemoteGitClient(repoUrl);
            return ActivatorUtilities.CreateInstance<Remote>(_serviceProvider, remoteGitClient);
        }
    }

    public async Task<IDependencyFileManager> CreateDependencyFileManagerAsync(string repoUrl)
    {
        using (_operations.BeginOperation($"Getting remote file manager for repo {repoUrl}."))
        {
            IRemoteGitRepo remoteGitClient = await GetRemoteGitClient(repoUrl);
            return ActivatorUtilities.CreateInstance<DependencyFileManager>(_serviceProvider, remoteGitClient);
        }
    }

    private async Task<IRemoteGitRepo> GetRemoteGitClient(string repoUrl)
    {
        // Normalize the url with the AzDO client prior to attempting to
        // get a token. When we do coherency updates we build a repo graph and
        // may end up traversing links to classic azdo uris.
        string normalizedUrl = AzureDevOpsClient.NormalizeUrl(repoUrl);

        long installationId = await _context.GetInstallationId(normalizedUrl);
        var repoType = GitRepoUrlUtils.ParseTypeFromUri(normalizedUrl);

        if (repoType == GitRepoType.GitHub && installationId == default)
        {
            throw new GithubApplicationInstallationException($"No installation is available for repository '{normalizedUrl}'");
        }

        return repoType switch
        {
            GitRepoType.GitHub => installationId == default
                ? throw new GithubApplicationInstallationException($"No installation is available for repository '{normalizedUrl}'")
                : new GitHubClient(
                    new Microsoft.DotNet.DarcLib.GitHubTokenProvider(_gitHubTokenProvider),
                    _processManager,
                    _loggerFactory.CreateLogger<GitHubClient>(),
                    _cache.Cache,
                    _redisCacheClient),

            GitRepoType.AzureDevOps => new AzureDevOpsClient(
                _azdoTokenProvider,
                _processManager,
                _loggerFactory.CreateLogger<AzureDevOpsClient>()),

            _ => throw new NotImplementedException($"Unknown repo url type {normalizedUrl}"),
        };
    }
}
