using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
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
        ///     Download and install git to a temporary location.
        ///     Git is used by DarcLib, and the Service Fabric nodes do not have it installed natively.
        ///     
        ///     The file is assumed to be on a public endpoint.
        ///     We return the git client executable so that this call may be easily wrapped in RetryAsync
        /// </summary>
        public async Task<string> GetPathToLocalGitAsync()
        {
            // Determine whether we need to do any downloading at all.
            if (!string.IsNullOrEmpty(_gitExecutable) && File.Exists(_gitExecutable))
            {
                _logger.LogInformation($"Git executable found at {_gitExecutable}. Checking the installation.");
                // We should also make sure that the git executable that exists runs properly
                try
                {
                    LocalHelpers.CheckGitInstallation(_gitExecutable, _logger);
                    return _gitExecutable;
                }
                catch
                { 
                    _logger.LogWarning($"Something went wrong with validating git executable at {_gitExecutable}. Downloading new version.");
                }
            }

            // First, make sure we're the only actor in the process doing this
            await _semaphoreSlim.WaitAsync();

            // Since the collisions are already using paths from _tempFiles.GetFilePath (instance-specific) we'll try
            // a deterministic modification of that for the name of the mutex (using a raw path as the name throws)
            string mutexName = _tempFiles.GetFilePath("git-download-mutex").Replace('/', '-').Replace('\\', '-').Replace(':', '-');

            using (var mutex = new Mutex(false, mutexName))
            {
                bool mutexAcquired = false;

                // The Portable Git is around ~120 MB so 5 minutes should be plenty of time for the other Actor to
                // download and unzip, otherwise we'll just throw/retry when the TimeoutException is thrown, allowing
                // even more time for the instance that is downloading.
                try
                {
                    // acquire the mutex (or timeout after 5 minutes)
                    // will return false if it timed out
                    mutexAcquired = mutex.WaitOne((int)TimeSpan.FromMinutes(5).TotalMilliseconds);
                }
                catch (AbandonedMutexException)
                {
                    // "abandoned" means still acquired
                    mutexAcquired = true;
                }

                // if it wasn't acquired, it timed out, throw so it goes to retry.
                if (!mutexAcquired)
                {
                    throw new TimeoutException("Timed out acquiring a Mutex for git download, will retry.");
                }

                string gitLocation = _configuration.GetValue<string>("GitDownloadLocation", null);
                string[] pathSegments = new Uri(gitLocation, UriKind.Absolute).Segments;
                string remoteFileName = pathSegments[pathSegments.Length - 1];

                // There are multiple checks whether Git Works, so if it's already there, we'll just rely on 
                // falling through into CheckGitInstallation()
                string gitRoot = _tempFiles.GetFilePath("git-portable");
                string targetPath = Path.Combine(gitRoot, Path.GetFileNameWithoutExtension(remoteFileName));
                _gitExecutable = Path.Combine(targetPath, "bin", "git.exe");

                try
                {
                    // Determine whether another process/ thread ended up getting the lock and downloaded git in the meantime.
                    if (!File.Exists(_gitExecutable))
                    {
                        using (_operations.BeginOperation($"Installing a local copy of git"))
                        {
                            string gitZipFile = Path.Combine(gitRoot, remoteFileName);

                            _logger.LogInformation($"Downloading git from '{gitLocation}' to '{gitZipFile}'");

                            if (Directory.Exists(targetPath))
                            {
                                _logger.LogInformation($"Target directory {targetPath} already exists. Deleting it.");

                                // https://github.com/dotnet/arcade/issues/7343
                                // If this continues to fail despite having both process/thread semaphores wrapping it, we should consider
                                // killing any git.exe whose image path resides under this folder or simply using another folder and wasting disk space.

                                // However, since we now only check for the executable's presence, if we somehow do just hang a git.exe or two,
                                // since we already know _gitExecutable we'll be able to just test if it runs.
                                Directory.Delete(targetPath, true);
                            }

                            Directory.CreateDirectory(targetPath);

                            using (HttpClient client = new HttpClient())
                            using (FileStream outStream = new FileStream(gitZipFile, FileMode.Create, FileAccess.Write))
                            using (var inStream = await client.GetStreamAsync(gitLocation))
                            {
                                await inStream.CopyToAsync(outStream);
                            }

                            _logger.LogInformation($"Extracting '{gitZipFile}' to '{targetPath}'");

                            ZipFile.ExtractToDirectory(gitZipFile, targetPath, overwriteFiles: true);
                        }
                    }
                }
                finally
                {
                    mutex.ReleaseMutex();
                    _semaphoreSlim.Release();
                }
            }

            // Will throw if something is wrong with the git executable, forcing a retry
            LocalHelpers.CheckGitInstallation(_gitExecutable, _logger);
            return _gitExecutable;
        }
    }
}
