// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Maestro.GitHub;
using Maestro.AzureDevOps;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Maestro.Data;
using System.IO;
using Microsoft.Extensions.Caching.Memory;

namespace SubscriptionActorService
{
    public class DarcRemoteFactory : IRemoteFactory
    {
        public DarcRemoteFactory(
            IConfigurationRoot configuration,
            IGitHubTokenProvider gitHubTokenProvider,
            IAzureDevOpsTokenProvider azureDevOpsTokenProvider,
            IMemoryCache memoryCache,
            BuildAssetRegistryContext context)
        {
            Configuration = configuration;
            GitHubTokenProvider = gitHubTokenProvider;
            AzureDevOpsTokenProvider = azureDevOpsTokenProvider;
            Cache = memoryCache;
            Context = context;
        }
        
        public IConfigurationRoot Configuration { get; }
        public IGitHubTokenProvider GitHubTokenProvider { get; }
        public IAzureDevOpsTokenProvider AzureDevOpsTokenProvider { get; }
        public BuildAssetRegistryContext Context { get; }
        public IMemoryCache Cache { get; set; }

        public Task<IRemote> GetBarOnlyRemoteAsync(ILogger logger)
        {
            return Task.FromResult((IRemote)new Remote(null, new MaestroBarClient(Context), logger));
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
                // Look up the setting for where the repo root should be held.  Default to empty,
                // which will use the temp directory.
                string temporaryRepositoryRoot = Configuration.GetValue<string>("DarcTemporaryRepoRoot", null);
                if (string.IsNullOrEmpty(temporaryRepositoryRoot))
                {
                    temporaryRepositoryRoot = Path.GetTempPath();
                }
                IGitRepo gitClient;

                long installationId = await Context.GetInstallationId(normalizedUrl);

                switch (normalizedRepoUri.Host)
                {
                    case "github.com":
                        if (installationId == default)
                        {
                            throw new SubscriptionException($"No installation is avaliable for repository '{normalizedUrl}'");
                        }

                        gitClient = new GitHubClient(await GitHubTokenProvider.GetTokenForInstallation(installationId),
                            logger, temporaryRepositoryRoot, Cache);
                        break;
                    case "dev.azure.com":
                        gitClient = new AzureDevOpsClient(await AzureDevOpsTokenProvider.GetTokenForRepository(normalizedUrl),
                            logger, temporaryRepositoryRoot);
                        break;
                    default:
                        throw new NotImplementedException($"Unknown repo url type {normalizedUrl}");
                };

                return new Remote(gitClient, new MaestroBarClient(Context), logger);
            }
        }
    }
}
