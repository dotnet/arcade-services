// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    public class Local : ILocal
    {
        private const string _branch = "current";
        private readonly GitFileManager _fileManager;
        private readonly IGitRepo _gitClient;

        private readonly ILogger _logger;

        // TODO: Make these not constants and instead attempt to give more accurate information commit, branch, repo name, etc.)
        private readonly string _repo;

        /// <summary>
        ///     Passed to the local helpers, causing git to be chosen from the path
        /// </summary>
        private const string GitExecutable = "git";

        public Local(ILogger logger, string overrideRootPath = null)
        {
            _repo = overrideRootPath ?? LocalHelpers.GetRootDir(GitExecutable, logger);
            _logger = logger;
            _gitClient = new LocalGitClient(GitExecutable, _logger);
            _fileManager = new GitFileManager(_gitClient, _logger);
        }

        /// <summary>
        ///     Adds a dependency to the dependency files
        /// </summary>
        /// <returns></returns>
        public async Task AddDependencyAsync(DependencyDetail dependency)
        {
            // TODO: https://github.com/dotnet/arcade/issues/1095
            // This should be getting back a container and writing the files from here.
            await _fileManager.AddDependencyAsync(dependency, _repo, null);
        }

        /// <summary>
        ///     Updates existing dependencies in the dependency files
        /// </summary>
        /// <param name="dependencies">Dependencies that need updates.</param>
        /// <param name="remote">Remote instance for gathering eng/common script updates.</param>
        /// <returns></returns>
        public async Task UpdateDependenciesAsync(List<DependencyDetail> dependencies, IRemoteFactory remoteFactory)
        {
            // Read the current dependency files and grab their locations so that nuget.config can be updated appropriately.
            // Update the incoming dependencies with locations.
            IEnumerable<DependencyDetail> oldDependencies = await GetDependenciesAsync();

            IRemote barOnlyRemote = await remoteFactory.GetBarOnlyRemoteAsync(_logger);
            await barOnlyRemote.AddAssetLocationToDependenciesAsync(oldDependencies);
            await barOnlyRemote.AddAssetLocationToDependenciesAsync(dependencies);

            var fileContainer = await _fileManager.UpdateDependencyFiles(dependencies, _repo, null, oldDependencies);
            List<GitFile> filesToUpdate = fileContainer.GetFilesToCommit();

            // TODO: This needs to be moved into some consistent handling between local/remote and add/update:
            // https://github.com/dotnet/arcade/issues/1095
            // If we are updating the arcade sdk we need to update the eng/common files as well
            DependencyDetail arcadeItem = dependencies.FirstOrDefault(
                i => string.Equals(i.Name, "Microsoft.DotNet.Arcade.Sdk", StringComparison.OrdinalIgnoreCase));

            if (arcadeItem != null)
            {
                try
                {
                    IRemote remote = await remoteFactory.GetRemoteAsync(arcadeItem.RepoUri, _logger);
                    List<GitFile> engCommonFiles = await remote.GetCommonScriptFilesAsync(arcadeItem.RepoUri, arcadeItem.Commit);
                    filesToUpdate.AddRange(engCommonFiles);

                    List<GitFile> localEngCommonFiles = await _gitClient.GetFilesAtCommitAsync(null, null, "eng/common");

                    foreach (GitFile file in localEngCommonFiles)
                    {
                        if (!engCommonFiles.Where(f => f.FilePath == file.FilePath).Any())
                        {
                            // This is a file in the repo's eng/common folder that isn't present in Arcade at the
                            // requested SHA so delete it during the update.
                            // GitFile instances do not have public setters since we insert/retrieve them from an
                            // In-memory cache during remote updates and we don't want anything to modify the cached,
                            // references, so add a copy with a Delete FileOperation.
                            filesToUpdate.Add(new GitFile(
                                file.FilePath,
                                file.Content,
                                file.ContentEncoding,
                                file.Mode,
                                GitFileOperation.Delete));
                        }
                    }
                }
                catch (Exception exc) when
                (exc.Message == "Not Found")
                {
                    _logger.LogWarning("Could not update 'eng/common'. Most likely this is a scenario " +
                        "where a packages folder was passed and the commit which generated them is not " +
                        "yet pushed.");
                }
            }

            // Push on local does not commit.
            await _gitClient.CommitFilesAsync(filesToUpdate, _repo, null, null);
        }

        /// <summary>
        ///     Gets the local dependencies
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<DependencyDetail>> GetDependenciesAsync(string name = null, bool includePinned = true, string branch = null)
        {
            return (await _fileManager.ParseVersionDetailsXmlAsync(_repo, branch, includePinned)).Where(
                dependency => string.IsNullOrEmpty(name) || dependency.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        ///     Verify the local repository has correct and consistent dependency information
        /// </summary>
        /// <returns>True if verification succeeds, false otherwise.</returns>
        public Task<bool> Verify()
        {
            return _fileManager.Verify(_repo, null);
        }

        /// <summary>
        /// Gets local dependencies from a local repository
        /// </summary>
        /// <returns></returns>
        public IEnumerable<DependencyDetail> GetDependenciesFromFileContents(string fileContents, bool includePinned = true)
        {
            return _fileManager.ParseVersionDetailsXml(fileContents, includePinned);
        }

        /// <summary>
        /// Checkout the local repo to a given state.
        /// </summary>
        /// <param name="commit">Tag, branch, or commit to checkout</param>
        public void Checkout(string commit, bool force = false)
        {
            _gitClient.Checkout(_repo, commit, force);
        }

        /// <summary>
        /// Create worktree in a given path and checkout the given commit-ish into it. 
        /// </summary>
        public void AddWorktree(string commitish, string name, string path, bool locked)
        {
            _gitClient.AddWorktree(_repo, commitish, name, path, locked);
        }

        /// <summary>
        /// Fetch all refs.
        /// </summary>
        public void Fetch()
        {
            _gitClient.Fetch(_repo);
        }

        /// <summary>
        /// Add a remote to the local repo if it does not already exist, and attempt to fetch new commits.
        /// </summary>
        /// <param name="repoDir">The directory of the local repo</param>
        /// <param name="repoUrl">The remote URL to add</param>
        public void AddRemoteIfMissing(string repoDir, string repoUrl)
        {
            _gitClient.AddRemoteIfMissing(repoDir, repoUrl);
        }
    }
}
