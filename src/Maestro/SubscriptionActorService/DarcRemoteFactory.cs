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

namespace SubscriptionActorService
{
    public class DarcRemoteFactory : IRemoteFactory
    {
        public DarcRemoteFactory(
            IConfigurationRoot configuration,
            IGitHubTokenProvider gitHubTokenProvider,
            IAzureDevOpsTokenProvider azureDevOpsTokenProvider,
            BuildAssetRegistryContext context)
        {
            Configuration = configuration;
            GitHubTokenProvider = gitHubTokenProvider;
            AzureDevOpsTokenProvider = azureDevOpsTokenProvider;
            Context = context;
        }
        
        public IConfigurationRoot Configuration { get; }
        public IGitHubTokenProvider GitHubTokenProvider { get; }
        public IAzureDevOpsTokenProvider AzureDevOpsTokenProvider { get; }
        public BuildAssetRegistryContext Context { get; }

        public Task<IRemote> GetBarOnlyRemoteAsync(ILogger logger)
        {
            return Task.FromResult((IRemote)new Remote(null, new MaestroBarClient(Context), logger));
        }

        public async Task<IRemote> GetRemoteAsync(string repoUrl, ILogger logger)
        {
            Uri repoUri = new Uri(repoUrl);
            // Look up the setting for where the repo root should be held.  Default to empty,
            // which will use the temp directory.
            string temporaryRepositoryRoot = Configuration.GetValue<string>("DarcTemporaryRepoRoot", null);
            if (string.IsNullOrEmpty(temporaryRepositoryRoot))
            {
                temporaryRepositoryRoot = Path.GetTempPath();
            }
            IGitRepo gitClient;

            long installationId = await Context.GetInstallationId(repoUrl);

            switch (repoUri.Host)
            {
                case "github.com":
                    if (installationId == default)
                    {
                        throw new SubscriptionException($"No installation is avaliable for repository '{repoUrl}'");
                    }

                    gitClient = new GitHubClient(await GitHubTokenProvider.GetTokenForInstallation(installationId),
                        logger, temporaryRepositoryRoot);
                    break;
                case "dev.azure.com":
                    gitClient = new AzureDevOpsClient(await AzureDevOpsTokenProvider.GetTokenForRepository(repoUrl),
                        logger, temporaryRepositoryRoot);
                    break;
                default:
                    throw new NotImplementedException($"Unknown repo url type {repoUrl}");
            };

            return new Remote(gitClient, new MaestroBarClient(Context), logger);
        }
    }
}
