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
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Logging;

namespace Maestro.DataProviders;

public class DarcRemoteFactory : IRemoteFactory
{
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly OperationManager _operations;

    public DarcRemoteFactory(
        BuildAssetRegistryContext context,
        IKustoClientProvider kustoClientProvider,
        IGitHubTokenProvider gitHubTokenProvider,
        IAzureDevOpsTokenProvider azureDevOpsTokenProvider,
        IVersionDetailsParser versionDetailsParser,
        DarcRemoteMemoryCache memoryCache,
        OperationManager operations)
    {
        _operations = operations;
        Context = context;
        KustoClientProvider = (KustoClientProvider)kustoClientProvider;
        GitHubTokenProvider = gitHubTokenProvider;
        AzureDevOpsTokenProvider = azureDevOpsTokenProvider;
        _versionDetailsParser = versionDetailsParser;
        Cache = memoryCache;
    }

    public BuildAssetRegistryContext Context { get; }

    private readonly KustoClientProvider KustoClientProvider;

    public DarcRemoteMemoryCache Cache { get; }

    public IGitHubTokenProvider GitHubTokenProvider { get; }

    public IAzureDevOpsTokenProvider AzureDevOpsTokenProvider { get; }

    public Task<IRemote> GetBarOnlyRemoteAsync(ILogger logger)
    {
        return Task.FromResult((IRemote)new Remote(null, new MaestroBarClient(Context, KustoClientProvider), _versionDetailsParser, logger));
    }

    public async Task<IRemote> GetRemoteAsync(string repoUrl, ILogger logger)
    {
        using (_operations.BeginOperation($"Getting remote for repo {repoUrl}."))
        {
            // Normalize the url with the AzDO client prior to attempting to
            // get a token. When we do coherency updates we build a repo graph and
            // may end up traversing links to classic azdo uris.
            string normalizedUrl = AzureDevOpsClient.NormalizeUrl(repoUrl);

            long installationId = await Context.GetInstallationId(normalizedUrl);
            var repoType = GitRepoTypeParser.ParseFromUri(normalizedUrl);

            if (repoType == GitRepoType.GitHub && installationId == default)
            {
                throw new GithubApplicationInstallationException($"No installation is available for repository '{normalizedUrl}'");
            }

            var remoteConfiguration = repoType switch
            {
                GitRepoType.GitHub => new RemoteConfiguration(
                    gitHubToken: await GitHubTokenProvider.GetTokenForInstallationAsync(installationId)),
                GitRepoType.AzureDevOps => new RemoteConfiguration(
                    azureDevOpsToken: await AzureDevOpsTokenProvider.GetTokenForRepository(normalizedUrl)),

                _ => throw new NotImplementedException($"Unknown repo url type {normalizedUrl}"),
            };

            IRemoteGitRepo remoteGitClient = repoType switch
            {
                GitRepoType.GitHub => installationId == default
                    ? throw new GithubApplicationInstallationException($"No installation is available for repository '{normalizedUrl}'")
                    : new GitHubClient(
                        gitExecutable: null,
                        remoteConfiguration.GitHubToken,
                        logger,
                        temporaryRepositoryPath: null,
                        Cache.Cache),

                GitRepoType.AzureDevOps => new AzureDevOpsClient(
                    gitExecutable: null,
                    remoteConfiguration.AzureDevOpsToken,
                    logger,
                    temporaryRepositoryPath: null),

                _ => throw new NotImplementedException($"Unknown repo url type {normalizedUrl}"),
            };

            return new Remote(remoteGitClient, new MaestroBarClient(Context, KustoClientProvider), _versionDetailsParser, logger);
        }
    }
}
