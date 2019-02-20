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

namespace SubscriptionActorService
{
    public class DarcRemoteFactory : IDarcRemoteFactory
    {
        public DarcRemoteFactory(
            ILoggerFactory loggerFactory,
            IConfigurationRoot configuration,
            IGitHubTokenProvider gitHubTokenProvider,
            IAzureDevOpsTokenProvider azureDevOpsTokenProvider)
        {
            LoggerFactory = loggerFactory;
            Configuration = configuration;
            GitHubTokenProvider = gitHubTokenProvider;
            AzureDevOpsTokenProvider = azureDevOpsTokenProvider;
        }

        public ILoggerFactory LoggerFactory { get; }
        public IConfigurationRoot Configuration { get; }
        public IGitHubTokenProvider GitHubTokenProvider { get; }
        public IAzureDevOpsTokenProvider AzureDevOpsTokenProvider { get; }

        public async Task<IRemote> CreateAsync(string repoUrl, long installationId)
        {
            Uri repoUri = new Uri(repoUrl);
            ILogger<IRemote> logger = LoggerFactory.CreateLogger<IRemote>();
            // Look up the setting for where the repo root should be held.  Default to empty,
            // which will use the temp directory.
            string temporaryRepositoryRoot = Configuration.GetValue<string>("DarcTemporaryRepoRoot", null);
            IGitRepo gitClient;

            switch (repoUri.Host)
            {
                case "github.com":
                    if (installationId == default)
                    {
                        throw new SubscriptionException($"No installation is avaliable for repository '{repoUrl}'");
                    }

                    gitClient = new GitHubClient(await GitHubTokenProvider.GetTokenForInstallation(installationId),
                                                 logger,
                                                 temporaryRepositoryRoot);
                    break;
                case "dev.azure.com":
                    gitClient = new AzureDevOpsClient(await AzureDevOpsTokenProvider.GetTokenForRepository(repoUrl),
                                                      logger,
                                                      temporaryRepositoryRoot);
                    break;
                default:
                    throw new NotImplementedException($"Unknown repo url type {repoUrl}");
            };

            return new Remote(gitClient, null, logger);
        }
    }
}
