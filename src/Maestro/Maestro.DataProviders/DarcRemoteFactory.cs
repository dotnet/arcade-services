// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.AzureDevOps;
using Maestro.Data;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Maestro.DataProviders
{
    public class DarcRemoteFactory : IRemoteFactory
    {
        public DarcRemoteFactory(
            BuildAssetRegistryContext context,
            IKustoClientProvider kustoClientProvider,
            IGitHubTokenProvider gitHubTokenProvider,
            IAzureDevOpsTokenProvider azureDevOpsTokenProvider,
            DarcRemoteMemoryCache memoryCache)
        {
            Context = context;
            KustoClientProvider = (KustoClientProvider)kustoClientProvider;
            GitHubTokenProvider = gitHubTokenProvider;
            AzureDevOpsTokenProvider = azureDevOpsTokenProvider;
            Cache = memoryCache;
        }

        public BuildAssetRegistryContext Context { get; }

        private readonly KustoClientProvider KustoClientProvider;

        public DarcRemoteMemoryCache Cache { get; }

        public IGitHubTokenProvider GitHubTokenProvider { get; }

        public IAzureDevOpsTokenProvider AzureDevOpsTokenProvider { get; }

        public Task<IRemote> GetBarOnlyRemoteAsync(ILogger logger)
        {
            return Task.FromResult((IRemote)new Remote(null, new MaestroBarClient(Context, KustoClientProvider), logger));
        }

        public async Task<IRemote> GetRemoteAsync(string repoUrl, ILogger logger)
        {
            using (logger.BeginScope($"Getting remote for repo {repoUrl}."))
            {
                // Normalize the url with the AzDO client prior to attempting to
                // get a token. When we do coherency updates we build a repo graph and
                // may end up traversing links to classic azdo uris.
                string normalizedUrl = AzureDevOpsClient.NormalizeUrl(repoUrl);
                Uri normalizedRepoUri = new Uri(normalizedUrl);

                IGitRepo gitClient;

                long installationId = await Context.GetInstallationId(normalizedUrl);

                switch (normalizedRepoUri.Host)
                {
                    case "github.com":
                        if (installationId == default)
                        {
                            throw new GithubApplicationInstallationException($"No installation is avaliable for repository '{normalizedUrl}'");
                        }
                        gitClient = new GitHubClient(null, await GitHubTokenProvider.GetTokenForInstallationAsync(installationId),
                            logger, null, Cache.Cache);
                        break;
                    case "dev.azure.com":
                        gitClient = new AzureDevOpsClient(null, await AzureDevOpsTokenProvider.GetTokenForRepository(normalizedUrl),
                            logger, null);
                        break;
                    default:
                        throw new NotImplementedException($"Unknown repo url type {normalizedUrl}");
                };

                return new Remote(gitClient, new MaestroBarClient(Context, KustoClientProvider), logger);
            }
        }
    }
}
