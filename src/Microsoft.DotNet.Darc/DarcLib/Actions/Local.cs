// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Microsoft.DotNet.DarcLib;

public class Local : ILocal
{
    private readonly DependencyFileManager _fileManager;
    private readonly ILocalLibGit2Client _gitClient;
    private readonly IVersionDetailsParser _versionDetailsParser;

    private readonly ILogger _logger;

    // TODO: Make these not constants and instead attempt to give more accurate information commit, branch, repo name, etc.)
    private readonly Lazy<string> _repoRootDir;

    /// <summary>
    ///     Passed to the local helpers, causing git to be chosen from the path
    /// </summary>
    private const string GitExecutable = "git";

    public Local(RemoteConfiguration remoteConfiguration, ILogger logger, string overrideRootPath = null)
    {
        _logger = logger;
        _versionDetailsParser = new VersionDetailsParser();
        _gitClient = new LocalLibGit2Client(remoteConfiguration, new ProcessManager(logger, GitExecutable), logger);
        _fileManager = new DependencyFileManager(_gitClient, _versionDetailsParser, logger);

        _repoRootDir = new(() => overrideRootPath ?? _gitClient.GetRootDirAsync().GetAwaiter().GetResult(), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>
    ///     Adds a dependency to the dependency files
    /// </summary>
    /// <returns></returns>
    public async Task AddDependencyAsync(DependencyDetail dependency)
    {
        // TODO: https://github.com/dotnet/arcade/issues/1095
        // This should be getting back a container and writing the files from here.
        await _fileManager.AddDependencyAsync(dependency, _repoRootDir.Value, null);
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

        // If we are updating the arcade sdk we need to update the eng/common files as well
        DependencyDetail arcadeItem = dependencies.FirstOrDefault(
            i => string.Equals(i.Name, "Microsoft.DotNet.Arcade.Sdk", StringComparison.OrdinalIgnoreCase));
        SemanticVersion targetDotNetVersion = null;
        IRemote arcadeRemote = null;

        if (arcadeItem != null)
        {
            arcadeRemote = await remoteFactory.GetRemoteAsync(arcadeItem.RepoUri, _logger);
            targetDotNetVersion = await arcadeRemote.GetToolsDotnetVersionAsync(arcadeItem.RepoUri, arcadeItem.Commit);
        }

        var fileContainer = await _fileManager.UpdateDependencyFiles(dependencies, _repoRootDir.Value, null, oldDependencies, targetDotNetVersion);
        List<GitFile> filesToUpdate = fileContainer.GetFilesToCommit();

        if (arcadeItem != null)
        {
            try
            {
                List<GitFile> engCommonFiles = await arcadeRemote.GetCommonScriptFilesAsync(arcadeItem.RepoUri, arcadeItem.Commit);
                filesToUpdate.AddRange(engCommonFiles);

                List<GitFile> localEngCommonFiles = GetFilesAtRelativeRepoPathAsync("eng/common");

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
        await _gitClient.CommitFilesAsync(filesToUpdate, _repoRootDir.Value, null, null);
    }

    /// <summary>
    ///     Gets the local dependencies
    /// </summary>
    /// <returns></returns>
    public async Task<IEnumerable<DependencyDetail>> GetDependenciesAsync(string name = null, bool includePinned = true)
    {
        return (await _fileManager.ParseVersionDetailsXmlAsync(_repoRootDir.Value, null, includePinned)).Where(
            dependency => string.IsNullOrEmpty(name) || dependency.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Verify the local repository has correct and consistent dependency information
    /// </summary>
    /// <returns>True if verification succeeds, false otherwise.</returns>
    public Task<bool> Verify()
    {
        return _fileManager.Verify(_repoRootDir.Value, null);
    }

    /// <summary>
    /// Gets local dependencies from a local repository
    /// </summary>
    /// <returns></returns>
    public IEnumerable<DependencyDetail> GetDependenciesFromFileContents(string fileContents, bool includePinned = true)
    {
        return _versionDetailsParser.ParseVersionDetailsXml(fileContents, includePinned);
    }

    /// <summary>
    /// Checkout the local repo to a given state.
    /// </summary>
    /// <param name="commit">Tag, branch, or commit to checkout</param>
    public void Checkout(string commit, bool force = false)
    {
        _gitClient.Checkout(_repoRootDir.Value, commit, force);
    }

    /// <summary>
    /// Add a remote to the local repo if it does not already exist, and attempt to fetch new commits.
    /// </summary>
    /// <param name="repoDir">The directory of the local repo</param>
    /// <param name="repoUrl">The remote URL to add</param>
    public async Task<string> AddRemoteIfMissingAsync(string repoDir, string repoUrl)
    {
        string remoteName = await _gitClient.AddRemoteIfMissingAsync(repoDir, repoUrl);
        await _gitClient.UpdateRemoteAsync(repoDir, remoteName);
        return remoteName;
    }

    private List<GitFile> GetFilesAtRelativeRepoPathAsync(string path)
    {
        string sourceFolder = Path.Combine(_repoRootDir.Value, path);
        var files = Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories);
        return files
            .Select(file => new GitFile(
                file.Remove(0, _repoRootDir.Value.Length + 1).Replace("\\", "/"),
                File.ReadAllText(file)))
            .ToList();
    }
}
