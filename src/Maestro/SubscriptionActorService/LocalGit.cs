using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SubscriptionActorService
{
    public interface ILocalGit
    {
        Task<string> GetPathToLocalGitAsync();
    }

    public class LocalGit : ILocalGit
    {
        private string _gitExecutable;
        private readonly TemporaryFiles _tempFiles;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
        private readonly IConfiguration _configuration;
        private readonly ILogger<LocalGit> _logger;
        private readonly OperationManager _operations;

        public LocalGit(TemporaryFiles tempFiles, IConfiguration configuration, OperationManager operations, ILogger<LocalGit> logger)
        {
            _tempFiles = tempFiles;
            _configuration = configuration;
            _logger = logger;
            _operations = operations;
        }
        /// <summary>
        ///     Download and install git to the a temporary location.
        ///     Git is used by DarcLib, and the Service Fabric nodes do not have it installed natively.
        ///     
        ///     The file is assumed to be on a public endpoint.
        ///     We return the git client executable so that this call may be easily wrapped in RetryAsync
        /// </summary>
        public async Task<string> GetPathToLocalGitAsync()
        {
            // Determine whether we need to do any downloading at all.
            if (!string.IsNullOrEmpty(_gitExecutable))
            {
                return _gitExecutable;
            }

            await _semaphoreSlim.WaitAsync();
            try
            {
                // Determine whether another thread ended up getting the lock and downloaded git
                // in the meantime.
                if (string.IsNullOrEmpty(_gitExecutable))
                {
                    using (_operations.BeginOperation($"Installing a local copy of git"))
                    {
                        string gitLocation = _configuration.GetValue<string>("GitDownloadLocation", null);
                        string[] pathSegments = new Uri(gitLocation, UriKind.Absolute).Segments;
                        string remoteFileName = pathSegments[pathSegments.Length - 1];

                        string gitRoot = _tempFiles.GetFilePath("git-portable");
                        string targetPath = Path.Combine(gitRoot, Path.GetFileNameWithoutExtension(remoteFileName));
                        string gitZipFile = Path.Combine(gitRoot, remoteFileName);

                        _logger.LogInformation($"Downloading git from '{gitLocation}' to '{gitZipFile}'");

                        Directory.CreateDirectory(targetPath);

                        using (HttpClient client = new HttpClient())
                        using (FileStream outStream = new FileStream(gitZipFile, FileMode.Create, FileAccess.Write))
                        using (var inStream = await client.GetStreamAsync(gitLocation))
                        {
                            await inStream.CopyToAsync(outStream);
                        }

                        _logger.LogInformation($"Extracting '{gitZipFile}' to '{targetPath}'");

                        ZipFile.ExtractToDirectory(gitZipFile, targetPath, overwriteFiles: true);

                        _gitExecutable = Path.Combine(targetPath, "bin", "git.exe");
                    }
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            return _gitExecutable;
        }
    }
}
