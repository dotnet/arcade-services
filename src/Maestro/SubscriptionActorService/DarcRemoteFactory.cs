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
            DarcSettings settings = new DarcSettings();
            Uri repoUri = new Uri(repoUrl);

            // Look up the setting for where the repo root should be held.  Default to empty,
            // which will use the temp directory.
            settings.TemporaryRepositoryRoot = Configuration.GetValue<string>("DarcTemporaryRepoRoot", null);

            switch (repoUri.Host)
            {
                case "github.com":
                    if (installationId == default)
                    {
                        throw new SubscriptionException($"No installation is avaliable for repository '{repoUrl}'");
                    }

                    settings.GitType = GitRepoType.GitHub;
                    settings.PersonalAccessToken = await GitHubTokenProvider.GetTokenForInstallation(installationId);
                    break;
                case "dev.azure.com":
                    settings.GitType = GitRepoType.AzureDevOps;
                    // Parse out the instance name and then grab the PAT via 
                    settings.PersonalAccessToken = await AzureDevOpsTokenProvider.GetTokenForRepository(repoUrl);
                    break;
                default:
                    throw new NotImplementedException($"Unknown repo url type {repoUrl}");
            };

            return new Remote(settings, LoggerFactory.CreateLogger<Remote>());
        }
    }
}
