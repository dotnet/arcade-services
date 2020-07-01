// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Maestro.AzureDevOps;
using Maestro.Data;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SubscriptionActorService
{
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
            OperationManager operations)
        {
            _tempFiles = tempFiles;
            _localGit = localGit;
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
        private readonly OperationManager _operations;


        public Task<IRemote> GetBarOnlyRemoteAsync(ILogger logger)
        {
            return Task.FromResult((IRemote)new Remote(null, new MaestroBarClient(_context), logger));
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
                IGitRepo gitClient;

                long installationId = await _context.GetInstallationId(normalizedUrl);

                var gitExe = await ExponentialRetry.RetryAsync(
                    async () => await _localGit.GetPathToLocalGitAsync(),
                    ex => logger.LogError(ex, $"Failed to install git to local temporary directory."),
                    ex => true);

                switch (normalizedRepoUri.Host)
                {
                    case "github.com":
                        if (installationId == default)
                        {
                            throw new GithubApplicationInstallationException($"No installation is avaliable for repository '{normalizedUrl}'");
                        }

                        gitClient = new GitHubClient(gitExe, await _gitHubTokenProvider.GetTokenForInstallationAsync(installationId),
                            logger, temporaryRepositoryRoot, _cache.Cache);
                        break;
                    case "dev.azure.com":
                        gitClient = new AzureDevOpsClient(gitExe, await _azureDevOpsTokenProvider.GetTokenForRepository(normalizedUrl),
                            logger, temporaryRepositoryRoot);
                        break;
                    default:
                        throw new NotImplementedException($"Unknown repo url type {normalizedUrl}");
                };

                return new Remote(gitClient, new MaestroBarClient(_context), logger);
            }
        }

    }
}
