// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maestro.AzureDevOps;
using Maestro.Data;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SubscriptionActorService
{
    public class DarcRemoteFactory : IRemoteFactory
    {
        public DarcRemoteFactory(
            IConfigurationRoot configuration,
            IGitHubTokenProvider gitHubTokenProvider,
            IAzureDevOpsTokenProvider azureDevOpsTokenProvider,
            DarcRemoteMemoryCache memoryCache,
            BuildAssetRegistryContext context,
            TemporaryFiles tempFiles)
        {
            _tempFiles = tempFiles;
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
        public DarcRemoteMemoryCache Cache { get; }

        private readonly TemporaryFiles _tempFiles;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
        
        private string _gitExecutable;

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
                    temporaryRepositoryRoot = _tempFiles.GetFilePath("repos");
                }
                IGitRepo gitClient;

                long installationId = await Context.GetInstallationId(normalizedUrl);

                await ExponentialRetry.RetryAsync(
                    async () => await EnsureLocalGit(logger),
                    ex => logger.LogError(ex, $"Failed to install git to local temporary directory."),
                    ex => true);

                switch (normalizedRepoUri.Host)
                {
                    case "github.com":
                        if (installationId == default)
                        {
                            throw new SubscriptionException($"No installation is avaliable for repository '{normalizedUrl}'");
                        }

                        gitClient = new GitHubClient(_gitExecutable, await GitHubTokenProvider.GetTokenForInstallationAsync(installationId),
                            logger, temporaryRepositoryRoot, Cache.Cache);
                        break;
                    case "dev.azure.com":
                        gitClient = new AzureDevOpsClient(_gitExecutable, await AzureDevOpsTokenProvider.GetTokenForRepository(normalizedUrl),
                            logger, temporaryRepositoryRoot);
                        break;
                    default:
                        throw new NotImplementedException($"Unknown repo url type {normalizedUrl}");
                };

                return new Remote(gitClient, new MaestroBarClient(Context), logger);
            }
        }

        /// <summary>
        ///     Download and install git to the a temporary location.
        ///     Git is used by DarcLib, and the Service Fabric nodes do not have it installed natively.
        ///     
        ///     The file is assumed to be on a public endpoint.
        ///     We return the git client executable so that this call may be easily wrapped in RetryAsync
        /// </summary>
        public async Task<string> EnsureLocalGit(ILogger logger)
        {
            // Determine whether we need to do any downloading at all.
            if (string.IsNullOrEmpty(_gitExecutable))
            {
                await _semaphoreSlim.WaitAsync();
                try
                {
                    // Determine whether another thread ended up getting the lock and downloaded git
                    // in the meantime.
                    if (string.IsNullOrEmpty(_gitExecutable))
                    {
                        using (logger.BeginScope($"Installing a local copy of git"))
                        {
                            string gitLocation = Configuration.GetValue<string>("GitDownloadLocation", null);
                            string[] pathSegments = new Uri(gitLocation, UriKind.Absolute).Segments;
                            string remoteFileName = pathSegments[pathSegments.Length - 1];

                            string gitRoot = _tempFiles.GetFilePath("git-portable");
                            string targetPath = Path.Combine(gitRoot, Path.GetFileNameWithoutExtension(remoteFileName));
                            string gitZipFile = Path.Combine(gitRoot, remoteFileName);

                            logger.LogInformation($"Downloading git from '{gitLocation}' to '{gitZipFile}'");

                            Directory.CreateDirectory(targetPath);

                            using (HttpClient client = new HttpClient())
                            using (FileStream outStream = new FileStream(gitZipFile, FileMode.Create, FileAccess.Write))
                            using (var inStream = await client.GetStreamAsync(gitLocation))
                            {
                                await inStream.CopyToAsync(outStream);
                            }

                            logger.LogInformation($"Extracting '{gitZipFile}' to '{targetPath}'");

                            ZipFile.ExtractToDirectory(gitZipFile, targetPath, overwriteFiles: true);

                            _gitExecutable = Path.Combine(targetPath, "bin", "git.exe");
                        }
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }

            return _gitExecutable;
        }
    }
}
