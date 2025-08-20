// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Microsoft.DotNet.DarcLib;

public class Local
{
    private readonly DependencyFileManager _fileManager;
    private readonly ILocalLibGit2Client _gitClient;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ILogger _logger;
    private readonly Lazy<string> _repoRootDir;

    /// <summary>
    ///     Passed to the local helpers, causing git to be chosen from the path
    /// </summary>
    private const string GitExecutable = "git";

    public Local(IRemoteTokenProvider tokenProvider, ILogger logger, string overrideRootPath = null)
    {
        _logger = logger;
        _versionDetailsParser = new VersionDetailsParser();
        _gitClient = new LocalLibGit2Client(tokenProvider, new NoTelemetryRecorder(), new ProcessManager(logger, GitExecutable), new FileSystem(), logger);
        _fileManager = new DependencyFileManager(_gitClient, _versionDetailsParser, logger);

        _repoRootDir = new(() => overrideRootPath ?? _gitClient.GetRootDirAsync().GetAwaiter().GetResult(), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>
    ///     Adds a dependency to the dependency files
    /// </summary>
    public async Task AddDependencyAsync(DependencyDetail dependency, UnixPath relativeBasePath = null)
    {
        await _fileManager.TryAddDependencyAsync(dependency, _repoRootDir.Value, null, relativeBasePath);
    }

    /// <summary>
    ///     Adds a dependency to the dependency files
    /// </summary>
    public async Task RemoveDependencyAsync(string dependencyName, UnixPath relativeBasePath = null)
    {
        await _fileManager.TryRemoveDependencyAsync(dependencyName, _repoRootDir.Value, null, relativeBasePath);
    }

    /// <summary>
    ///     Updates existing dependencies in the dependency files
    /// </summary>
    /// <param name="dependencies">Dependencies that need updates.</param>
    public async Task UpdateDependenciesAsync(List<DependencyDetail> dependencies, IRemoteFactory remoteFactory, IGitRepoFactory gitRepoFactory, IBasicBarClient barClient)
    {
        // Read the current dependency files and grab their locations so that nuget.config can be updated appropriately.
        // Update the incoming dependencies with locations.
        List<DependencyDetail> oldDependencies = await GetDependenciesAsync();

        var locationResolver = new AssetLocationResolver(barClient);
        await locationResolver.AddAssetLocationToDependenciesAsync(oldDependencies);
        await locationResolver.AddAssetLocationToDependenciesAsync(dependencies);

        // If we are updating the arcade sdk we need to update the eng/common files as well
        DependencyDetail arcadeItem = dependencies.GetArcadeUpdate();
        SemanticVersion targetDotNetVersion = null;
        var repoIsVmr = true;
        var relativeBasePath = VmrInfo.ArcadeRepoDir;

        if (arcadeItem != null)
        {
            var fileManager = new DependencyFileManager(gitRepoFactory, _versionDetailsParser, _logger);
            try
            {
                targetDotNetVersion = await fileManager.ReadToolsDotnetVersionAsync(arcadeItem.RepoUri, arcadeItem.Commit, relativeBasePath);
            }
            catch (DependencyFileNotFoundException)
            {
                // global.json not found in src/arcade meaning that repo is not the VMR
                repoIsVmr = false;
                relativeBasePath = null;
                targetDotNetVersion = await fileManager.ReadToolsDotnetVersionAsync(arcadeItem.RepoUri, arcadeItem.Commit, relativeBasePath);
            }
        }

        var fileContainer = await _fileManager.UpdateDependencyFiles(dependencies, sourceDependency: null, _repoRootDir.Value, null, oldDependencies, targetDotNetVersion);
        List<GitFile> filesToUpdate = fileContainer.GetFilesToCommit();

        if (arcadeItem != null)
        {
            try
            {
                IRemote arcadeRemote = await remoteFactory.CreateRemoteAsync(arcadeItem.RepoUri);
                List<GitFile> engCommonFiles = await arcadeRemote.GetCommonScriptFilesAsync(arcadeItem.RepoUri, arcadeItem.Commit, relativeBasePath);
                // If we're updating arcade from a VMR build, the eng/common files will be in the src/arcade repo
                // so we need to strip the src/arcade prefix from the file paths.
                if (repoIsVmr)
                {
                    engCommonFiles = engCommonFiles
                        .Select(f => new GitFile(
                            f.FilePath.Replace("src/arcade/", null),
                            f.Content,
                            f.ContentEncoding,
                            f.Mode,
                            f.Operation))
                        .ToList();
                }

                filesToUpdate.AddRange(engCommonFiles);

                List<GitFile> localEngCommonFiles = GetFilesAtRelativeRepoPathAsync(Constants.CommonScriptFilesPath);

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
    public async Task<List<DependencyDetail>> GetDependenciesAsync(string name = null, bool includePinned = true)
    {
        VersionDetails versionDetails = await _fileManager.ParseVersionDetailsXmlAsync(_repoRootDir.Value, null, includePinned);
        return versionDetails.Dependencies
            .Where(dependency => string.IsNullOrEmpty(name) || dependency.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
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
    public IEnumerable<DependencyDetail> GetDependenciesFromFileContents(string fileContents, bool includePinned = true)
    {
        return _versionDetailsParser.ParseVersionDetailsXml(fileContents, includePinned).Dependencies;
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
        var remoteName = await _gitClient.AddRemoteIfMissingAsync(repoDir, repoUrl);
        await _gitClient.UpdateRemoteAsync(repoDir, remoteName);
        return remoteName;
    }

    private List<GitFile> GetFilesAtRelativeRepoPathAsync(string path)
    {
        var sourceFolder = Path.Combine(_repoRootDir.Value, path);
        var files = Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories);
        return files
            .Select(file => new GitFile(
                file.Remove(0, _repoRootDir.Value.Length + 1).Replace("\\", "/"),
                File.ReadAllText(file)))
            .ToList();
    }
}
