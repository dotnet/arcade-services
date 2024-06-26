// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is able to update an individual repository within the VMR from one commit to another.
/// It creates git diffs while adhering to cloaking rules, accommodating for patched files, resolving submodules.
/// It can also update other repositories recursively based on the dependencies stored in Version.Details.xml.
/// </summary>
public class VmrUpdater : VmrManagerBase, IVmrUpdater
{
    // Message used when synchronizing multiple commits as one
    private const string SquashCommitMessage =
        $$"""
        [{name}] Sync {oldShaShort}{{Constants.Arrow}}{newShaShort}
        Diff: {remote}/compare/{oldSha}..{newSha}
        
        From: {remote}/commit/{oldSha}
        To: {remote}/commit/{newSha}
        
        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    // Message used when finalizing the sync with a merge commit
    private const string MergeCommitMessage =
        $$"""
        [Recursive sync] {name} / {oldShaShort}{{Constants.Arrow}}{newShaShort}
        
        Updated repositories:
        {commitMessage}
        
        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrUpdater> _logger;
    private readonly ISourceManifest _sourceManifest;
    private readonly IThirdPartyNoticesGenerator _thirdPartyNoticesGenerator;
    private readonly IComponentListGenerator _readmeComponentListGenerator;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly IWorkBranchFactory _workBranchFactory;

    // The VMR SHA before the synchronization has started
    private string? _startingVmrSha;

    public VmrUpdater(
        IVmrDependencyTracker dependencyTracker,
        IVersionDetailsParser versionDetailsParser,
        IRepositoryCloneManager cloneManager,
        IVmrPatchHandler patchHandler,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        IComponentListGenerator readmeComponentListGenerator,
        ICodeownersGenerator codeownersGenerator,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IDependencyFileManager dependencyFileManager,
        IGitRepoFactory gitRepoFactory,
        IWorkBranchFactory workBranchFactory,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger,
        ISourceManifest sourceManifest,
        IVmrInfo vmrInfo)
        : base(vmrInfo, sourceManifest, dependencyTracker, patchHandler, versionDetailsParser, thirdPartyNoticesGenerator, readmeComponentListGenerator, codeownersGenerator, localGitClient, localGitRepoFactory, dependencyFileManager, fileSystem, logger)
    {
        _logger = logger;
        _sourceManifest = sourceManifest;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _cloneManager = cloneManager;
        _patchHandler = patchHandler;
        _fileSystem = fileSystem;
        _thirdPartyNoticesGenerator = thirdPartyNoticesGenerator;
        _readmeComponentListGenerator = readmeComponentListGenerator;
        _gitRepoFactory = gitRepoFactory;
        _workBranchFactory = workBranchFactory;
    }

    public async Task<bool> UpdateRepository(
        string mappingName,
        string? targetRevision,
        string? targetVersion,
        bool updateDependencies,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? componentTemplatePath,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        await _dependencyTracker.InitializeSourceMappings();

        _startingVmrSha = await LocalVmr.GetGitCommitAsync(cancellationToken);

        var mapping = _dependencyTracker.GetMapping(mappingName);

        // Reload source-mappings.json if it's getting updated
        if (_vmrInfo.SourceMappingsPath != null
            && targetRevision != null
            && _vmrInfo.SourceMappingsPath.StartsWith(VmrInfo.GetRelativeRepoSourcesPath(mapping)))
        {
            var relativePath = _vmrInfo.SourceMappingsPath.Substring(VmrInfo.GetRelativeRepoSourcesPath(mapping).Length);
            mapping = await LoadNewSourceMappings(mapping, relativePath, targetRevision, additionalRemotes);
        }

        var dependencyUpdate = new VmrDependencyUpdate(
            mapping,
            mapping.DefaultRemote,
            targetRevision ?? mapping.DefaultRef,
            targetVersion,
            Parent: null);

        bool hadUpdates;
        try
        {
            if (updateDependencies)
            {
                hadUpdates = await UpdateRepositoriesRecursively(
                    dependencyUpdate,
                    additionalRemotes,
                    componentTemplatePath,
                    tpnTemplatePath,
                    generateCodeowners,
                    discardPatches,
                    cancellationToken);
            }
            else
            {
                hadUpdates = await UpdateRepositoryInternal(
                    dependencyUpdate,
                    reapplyVmrPatches: true,
                    additionalRemotes,
                    componentTemplatePath,
                    tpnTemplatePath,
                    generateCodeowners,
                    discardPatches,
                    cancellationToken);

                await ReapplyVmrPatchesAsync(cancellationToken);
                await CommitAsync("[VMR patches] Re-apply VMR patches");

            }
        }
        catch (EmptySyncException e)
        {
            _logger.LogInformation(e.Message);
            return false;
        }

        return hadUpdates;
    }

    private async Task<bool> UpdateRepositoryInternal(
        VmrDependencyUpdate update,
        bool reapplyVmrPatches,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? componentTemplatePath,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        VmrDependencyVersion currentVersion = _dependencyTracker.GetDependencyVersion(update.Mapping)
            ?? throw new Exception($"Failed to find current version for {update.Mapping.Name}");

        // Do we need to change anything?
        if (currentVersion.Sha == update.TargetRevision)
        {
            if (currentVersion.PackageVersion != update.TargetVersion && update.TargetVersion != null)
            {
                await UpdateTargetVersionOnly(
                    update.TargetRevision,
                    update.TargetVersion,
                    update.Mapping,
                    currentVersion,
                    cancellationToken);
                return true;
            }

            return false;
        }

        _logger.LogInformation("Synchronizing {name} from {current} to {repo} / {revision}",
            update.Mapping.Name,
            currentVersion.Sha,
            update.RemoteUri,
            update.TargetRevision);

        // Sort remotes so that we go Local -> GitHub -> AzDO
        // This makes the synchronization work even for cases when we can't access internal repos
        // For example, when we merge internal branches to public and the commits are already in public,
        // even though Version.Details.xml or source-manifest.json point to internal AzDO ones, we can still synchronize.
        var remotes = additionalRemotes
            .Where(r => r.Mapping == update.Mapping.Name)
            .Select(r => r.RemoteUri)
            // Add remotes for where we synced last from and where we are syncing to (e.g. github.com -> dev.azure.com)
            .Append(_sourceManifest.GetRepoVersion(update.Mapping.Name).RemoteUri)
            .Append(update.RemoteUri)
            // Add the default remote
            .Prepend(update.Mapping.DefaultRemote)
            .Distinct()
            // Prefer local git repos, then GitHub, then AzDO
            .OrderRemotesByLocalPublicOther()
            .ToArray();

        ILocalGitRepo clone = await _cloneManager.PrepareCloneAsync(
            update.Mapping,
            remotes,
            requestedRefs: new[] { currentVersion.Sha, update.TargetRevision },
            checkoutRef: update.TargetRevision,
            cancellationToken);

        update = update with
        {
            TargetRevision = await clone.GetShaForRefAsync(update.TargetRevision)
        };

        _logger.LogInformation("Updating {repo} from {current} to {next}..",
            update.Mapping.Name, Commit.GetShortSha(currentVersion.Sha), Commit.GetShortSha(update.TargetRevision));

        await StageRepositoryUpdatesAsync(
            update,
            clone,
            additionalRemotes,
            currentVersion.Sha,
            componentTemplatePath,
            tpnTemplatePath,
            generateCodeowners,
            discardPatches,
            cancellationToken);

        if (reapplyVmrPatches)
        {
            await ReapplyVmrPatchesAsync(cancellationToken);
        }

        var commitMessage = PrepareCommitMessage(
            SquashCommitMessage,
            update.Mapping.Name,
            update.RemoteUri,
            currentVersion.Sha,
            update.TargetRevision);

        await CommitAsync(commitMessage);

        return true;
    }
    /// <summary>
    /// Updates a repository and all of it's dependencies recursively starting with a given mapping.
    /// Always updates to the first version found per repository in the dependency tree.
    /// </summary>
    private async Task<bool> UpdateRepositoriesRecursively(
        VmrDependencyUpdate rootUpdate,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? componentTemplatePath,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        // Synchronization creates commits (one per mapping and some extra)
        // on a separate branch that is then merged into original one
        var originalRootSha = Commit.GetShortSha(GetCurrentVersion(rootUpdate.Mapping));
        var workBranchName = "sync" +
            $"/{rootUpdate.Mapping.Name}" +
            $"/{originalRootSha}-{rootUpdate.TargetRevision}";
        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(LocalVmr, workBranchName);

        await RestoreVmrPatchedFilesAsync(cancellationToken);
        await CommitAsync("[VMR patches] Removed VMR patches");

        HashSet<VmrDependencyUpdate> updatedDependencies;
        try
        {
            updatedDependencies = await UpdateRepositoryRecursively(
                rootUpdate,
                additionalRemotes,
                componentTemplatePath,
                tpnTemplatePath,
                generateCodeowners,
                discardPatches,
                cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                InterruptedSyncExceptionMessage,
                workBranch.OriginalBranch.StartsWith("sync") || workBranch.OriginalBranch.StartsWith("init")
                    ? "the original"
                    : workBranch.OriginalBranch);
            throw;
        }

        await ReapplyVmrPatchesAsync(cancellationToken);
        await CommitAsync("[VMR patches] Re-apply VMR patches");
        await CleanUpRemovedRepos(componentTemplatePath, tpnTemplatePath);

        var summaryMessage = new StringBuilder();

        foreach (var update in updatedDependencies)
        {
            if (update.TargetRevision == update.TargetVersion)
            {
                continue;
            }

            var fromShort = Commit.GetShortSha(update.TargetVersion);
            var toShort = Commit.GetShortSha(update.TargetRevision);
            summaryMessage
                .AppendLine($"  - {update.Mapping.Name} / {fromShort}{Constants.Arrow}{toShort}")
                .AppendLine($"    {update.RemoteUri}/compare/{update.TargetVersion}..{update.TargetRevision}");
        }

        var commitMessage = PrepareCommitMessage(
            MergeCommitMessage,
            rootUpdate.Mapping.Name,
            rootUpdate.Mapping.DefaultRemote,
            originalRootSha,
            rootUpdate.TargetRevision,
            summaryMessage.ToString());

        await workBranch.MergeBackAsync(commitMessage);

        _logger.LogInformation("Recursive update for {repo} finished.{newLine}{message}",
            rootUpdate.Mapping.Name,
            Environment.NewLine,
            summaryMessage);

        return updatedDependencies.Any();
    }

    /// <summary>
    /// Updates a repository and all of it's dependencies recursively starting with a given mapping.
    /// Always updates to the first version found per repository in the dependency tree.
    /// </summary>
    private async Task<HashSet<VmrDependencyUpdate>> UpdateRepositoryRecursively(
        VmrDependencyUpdate rootUpdate,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? componentTemplatePath,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        string originalRootSha = GetCurrentVersion(rootUpdate.Mapping);

        _logger.LogInformation("Recursive update for {repo} / {from}{arrow}{to}",
            rootUpdate.Mapping.Name,
            Commit.GetShortSha(originalRootSha),
            Constants.Arrow,
            rootUpdate.TargetRevision);

        var updates = (await GetAllDependenciesAsync(rootUpdate, additionalRemotes, cancellationToken)).ToList();

        var extraneousMappings = _dependencyTracker.Mappings
            .Where(mapping => !updates.Any(update => update.Mapping == mapping))
            .Select(mapping => mapping.Name);

        if (extraneousMappings.Any())
        {
            var separator = $"{Environment.NewLine}  - ";
            _logger.LogWarning($"The following mappings do not appear in current update's dependency tree:{separator}{{extraMappings}}",
                string.Join(separator, extraneousMappings));
        }

        // Dependencies that were already updated during this run
        var updatedDependencies = new HashSet<VmrDependencyUpdate>();

        foreach (VmrDependencyUpdate update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string currentSha;
            try
            {
                currentSha = GetCurrentVersion(update.Mapping);
            }
            catch (RepositoryNotInitializedException)
            {
                _logger.LogWarning("Dependency {repo} has not been initialized in the VMR yet, repository will be initialized",
                    update.Mapping.Name);

                currentSha = Constants.EmptyGitObject;
                _dependencyTracker.UpdateDependencyVersion(update with
                {
                    TargetRevision = currentSha
                });
            }

            if (update.Parent is not null && currentSha != update.TargetRevision)
            {
                _logger.LogInformation("Recursively updating {parent}'s dependency {repo} / {from}{arrow}{to}",
                    update.Parent.Name,
                    update.Mapping.Name,
                    currentSha,
                    Constants.Arrow,
                    update.TargetRevision);
            }

            try
            {
                await UpdateRepositoryInternal(
                    update,
                    reapplyVmrPatches: false,
                    additionalRemotes,
                    componentTemplatePath,
                    tpnTemplatePath,
                    generateCodeowners,
                    discardPatches,
                    cancellationToken);
            }
            catch (EmptySyncException e) when (e.Message.Contains("is already at"))
            {
                if (update.Mapping == rootUpdate.Mapping)
                {
                    _logger.LogWarning(e.Message);
                }
                else
                {
                    _logger.LogInformation(e.Message);
                }

                continue;
            }
            catch (EmptySyncException e)
            {
                _logger.LogWarning(e.Message);
                continue;
            }

            updatedDependencies.Add(update with
            {
                // We resolve the SHA again because original target could have been a branch name
                TargetRevision = GetCurrentVersion(update.Mapping),

                // We also store the original SHA (into the version!) so that later we use it in the commit message
                TargetVersion = currentSha,
            });
        }

        return updatedDependencies;
    }

    /// <summary>
    /// Removes all VMR patches from the working tree so that repository changes can be ingested.
    /// </summary>
    private async Task RestoreVmrPatchedFilesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring all VMR patches before we ingest new changes...");

        IReadOnlyCollection<VmrIngestionPatch> vmrPatchesToRestore = await _patchHandler.GetVmrPatches(_startingVmrSha, cancellationToken);

        if (vmrPatchesToRestore.Count == 0)
        {
            _logger.LogInformation("No VMR patches found");
            return;
        }

        foreach (var patch in vmrPatchesToRestore)
        {
            _logger.LogInformation("Restoring VMR patch {patch}", patch.Path);
            await _patchHandler.ApplyPatch(
                patch,
                _vmrInfo.VmrPath / (patch.ApplicationPath ?? string.Empty),
                removePatchAfter: true,
                reverseApply: true,
                cancellationToken);
        }

        // Patches are reversed directly in index so we need to reset the working tree
        await LocalVmr.ResetWorkingTree();

        _logger.LogInformation("Files affected by VMR patches restored");
    }

    private string GetCurrentVersion(SourceMapping mapping)
    {
        var version = _dependencyTracker.GetDependencyVersion(mapping)
            ?? throw new RepositoryNotInitializedException($"Repository {mapping.Name} has not been initialized yet");

        return version.Sha;
    }

    private async Task CleanUpRemovedRepos(string? componentTemplatePath, string? tpnTemplatePath)
    {
        var deletedRepos = _sourceManifest
            .Repositories
            .Where(r => !_dependencyTracker.TryGetMapping(r.Path, out _))
            .ToList();

        if (!deletedRepos.Any())
        {
            return;
        }

        foreach (var repo in deletedRepos)
        {
            _logger.LogWarning("The mapping for {name} was deleted. Removing the repository from the VMR.", repo.Path);
            DeleteRepository(repo.Path);
        }

        var sourceManifestPath = _vmrInfo.SourceManifestPath;
        _fileSystem.WriteToFile(sourceManifestPath, _sourceManifest.ToJson());

        if (componentTemplatePath != null)
        {
            await _readmeComponentListGenerator.UpdateComponentList(componentTemplatePath);
        }

        if (tpnTemplatePath != null)
        {
            await _thirdPartyNoticesGenerator.UpdateThirdPartyNotices(tpnTemplatePath);
        }

        await LocalVmr.StageAsync(["*"]);
        var commitMessage = "Delete " + string.Join(", ", deletedRepos.Select(r => r.Path));
        await CommitAsync(commitMessage);
    }

    private void DeleteRepository(string repo)
    {
        var repoSourcesDir = _vmrInfo.GetRepoSourcesPath(repo);
        try
        {
            _fileSystem.DeleteDirectory(repoSourcesDir, true);
            _logger.LogInformation("Deleted directory {dir}", repoSourcesDir);
        }
        catch (DirectoryNotFoundException)
        {
            _logger.LogInformation("Directory {dir} is already deleted", repoSourcesDir);
        }

        _sourceManifest.RemoveRepository(repo);
        _logger.LogInformation("Removed record for repository {name} from {file}", repo, _vmrInfo.SourceManifestPath);

        if (_dependencyTracker.RemoveRepositoryVersion(repo))
        {
            _logger.LogInformation("Deleted {repo} version information from git-info", repo);
        } 
        else
        {
            _logger.LogInformation("{repo} version information is already deleted", repo);
        }
    }

    /// <summary>
    /// This method is called in cases when a repository is already at the target revision but the package version
    /// differs. This can happen when a new build from the same commit is synchronized to the VMR.
    /// </summary>
    private async Task UpdateTargetVersionOnly(
        string targetRevision,
        string targetVersion,
        SourceMapping mapping,
        VmrDependencyVersion currentVersion,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Repository {repo} is already at {sha} but differs in package version ({old} vs {new}). Updating metadata...",
            mapping.Name,
            targetRevision,
            currentVersion.PackageVersion,
            targetVersion);

        _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
            Mapping: mapping,
            TargetRevision: targetRevision,
            TargetVersion: targetVersion,
            Parent: null,
            RemoteUri: _sourceManifest.GetRepoVersion(mapping.Name).RemoteUri));

        var filesToAdd = new List<string>
        {
            VmrInfo.GitInfoSourcesDir,
            _vmrInfo.SourceManifestPath
        };

        cancellationToken.ThrowIfCancellationRequested();
        await LocalVmr.StageAsync(filesToAdd, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await CommitAsync($"Updated package version of {mapping.Name} to {targetVersion}", author: null);
    }

    private async Task<SourceMapping> LoadNewSourceMappings(
        SourceMapping mapping,
        string relativePath,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes)
    {
        var remotes = additionalRemotes
            .Where(r => r.Mapping == mapping.Name)
            .Select(r => r.RemoteUri)
            .Prepend(mapping.DefaultRemote);

        string? sourceMappingContent = null;

        foreach (var remote in remotes)
        {
            IGitRepo gitClient = _gitRepoFactory.CreateClient(remote);

            try
            {
                _logger.LogDebug("Looking for a new version of {file} in {repo}", relativePath, remote);
                sourceMappingContent = await gitClient.GetFileContentsAsync(relativePath, remote, targetRevision);

                if (sourceMappingContent != null)
                {
                    _logger.LogDebug("Found new version of {file} in {repo}", relativePath, remote);
                    break;
                }
            }
            catch
            {
                _logger.LogDebug("Failed to find {revision} in {repo}", targetRevision, remote);
            }
        }

        if (sourceMappingContent is null)
        {
            throw new Exception($"Failed to find version {targetRevision} of {relativePath} in any of the {mapping.Name} remotes");
        }

        _logger.LogDebug($"Loading a new version of source mappings...");

        var tempFile = _fileSystem.GetTempFileName();

        try
        {
            _fileSystem.WriteToFile(tempFile, sourceMappingContent);
            await _dependencyTracker.InitializeSourceMappings(tempFile);
            _logger.LogInformation("Initialized a new version of {file}", relativePath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to initialize a new version of {file}", relativePath);
        }
        finally
        {
            _fileSystem.DeleteFile(tempFile);
        }

        return _dependencyTracker.GetMapping(mapping.Name);
    }

    private class RepositoryNotInitializedException(string message) : Exception(message)
    {
    }
}
