// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is able to update an individual repository within the VMR from one commit to another.
/// It creates git diffs while adhering to cloaking rules, accommodating for patched files, resolving submodules.
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

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrUpdater> _logger;
    private readonly ISourceManifest _sourceManifest;
    private readonly IGitRepoFactory _gitRepoFactory;

    public VmrUpdater(
        IVmrDependencyTracker dependencyTracker,
        IRepositoryCloneManager cloneManager,
        IVmrPatchHandler patchHandler,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        ICodeownersGenerator codeownersGenerator,
        ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IGitRepoFactory gitRepoFactory,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger,
        ISourceManifest sourceManifest,
        IVmrInfo vmrInfo)
        : base(vmrInfo, dependencyTracker, patchHandler, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, fileSystem, logger)
    {
        _logger = logger;
        _sourceManifest = sourceManifest;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _cloneManager = cloneManager;
        _patchHandler = patchHandler;
        _fileSystem = fileSystem;
        _gitRepoFactory = gitRepoFactory;
    }

    public async Task<bool> UpdateRepository(
        string mappingName,
        string? targetRevision,
        CodeFlowParameters codeFlowParameters,
        bool resetToRemoteWhenCloningRepo = false,
        CancellationToken cancellationToken = default)
    {
        await _dependencyTracker.RefreshMetadata();

        var mapping = _dependencyTracker.GetMapping(mappingName);

        string? officialBuildId = null;
        int? barId = null;

        // Reload source-mappings.json if it's getting updated
        if (_vmrInfo.SourceMappingsPath != null
            && targetRevision != null
            && _vmrInfo.SourceMappingsPath.StartsWith(VmrInfo.GetRelativeRepoSourcesPath(mapping)))
        {
            var relativePath = _vmrInfo.SourceMappingsPath.Substring(VmrInfo.GetRelativeRepoSourcesPath(mapping).Length);
            mapping = await LoadNewSourceMappings(mapping, relativePath, targetRevision, codeFlowParameters.AdditionalRemotes);
        }

        var dependencyUpdate = new VmrDependencyUpdate(
            mapping,
            mapping.DefaultRemote,
            targetRevision ?? mapping.DefaultRef,
            Parent: null,
            officialBuildId,
            barId);

        try
        {
            IReadOnlyCollection<VmrIngestionPatch> patchesToReapply = await UpdateRepositoryInternal(
                dependencyUpdate,
                restoreVmrPatches: true,
                codeFlowParameters,
                resetToRemoteWhenCloningRepo,
                cancellationToken);

            await ReapplyVmrPatchesAsync(patchesToReapply, cancellationToken);
            return true;
        }
        catch (EmptySyncException e)
        {
            _logger.LogInformation(e.Message);
            return false;
        }
    }

    private async Task<IReadOnlyCollection<VmrIngestionPatch>> UpdateRepositoryInternal(
        VmrDependencyUpdate update,
        bool restoreVmrPatches,
        CodeFlowParameters codeFlowParameters,
        bool resetToRemoteWhenCloningRepo = false,
        CancellationToken cancellationToken = default)
    {
        VmrDependencyVersion currentVersion = _dependencyTracker.GetDependencyVersion(update.Mapping)
            ?? throw new Exception($"Failed to find current version for {update.Mapping.Name}");

        // Do we need to change anything?
        if (currentVersion.Sha == update.TargetRevision)
        {
            throw new EmptySyncException($"Repository {update.Mapping.Name} is already at {update.TargetRevision}");
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
        var remotes = codeFlowParameters.AdditionalRemotes
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
            resetToRemoteWhenCloningRepo,
            cancellationToken);

        update = update with
        {
            TargetRevision = await clone.GetShaForRefAsync(update.TargetRevision)
        };

        _logger.LogInformation("Updating {repo} from {current} to {next}..",
            update.Mapping.Name, Commit.GetShortSha(currentVersion.Sha), Commit.GetShortSha(update.TargetRevision));

        var commitMessage = PrepareCommitMessage(
            SquashCommitMessage,
            update.Mapping.Name,
            update.RemoteUri,
            currentVersion.Sha,
            update.TargetRevision);

        return await UpdateRepoToRevisionAsync(
            update,
            clone,
            currentVersion.Sha,
            commitMessage,
            restoreVmrPatches,
            codeFlowParameters,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Removes changes applied by VMR patches and restores the original state of the files.
    /// </summary>
    /// <param name="patches">Patches with incoming changes to be checked whether they affect some VMR patch</param>
    protected override async Task<IReadOnlyCollection<VmrIngestionPatch>> StripVmrPatchesAsync(
        IReadOnlyCollection<VmrIngestionPatch> patches,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<VmrIngestionPatch> vmrPatchesToRestore = await GetVmrPatches(
            patches,
            cancellationToken);

        if (vmrPatchesToRestore.Count == 0)
        {
            return vmrPatchesToRestore;
        }

        foreach (var patch in vmrPatchesToRestore.OrderByDescending(p => p.Path))
        {
            if (!_fileSystem.FileExists(patch.Path))
            {
                // Patch is being added, so it doesn't exist yet
                _logger.LogDebug("Not restoring {patch} as it will be added during the sync", patch.Path);
                continue;
            }

            await _patchHandler.ApplyPatch(
                patch,
                _vmrInfo.VmrPath,
                removePatchAfter: false,
                reverseApply: true,
                cancellationToken);
        }

        // Patches are reversed directly in index so we need to reset the working tree
        await GetLocalVmr().ResetWorkingTree();

        _logger.LogInformation("Files affected by VMR patches restored");

        return vmrPatchesToRestore;
    }

    /// <summary>
    /// Gets a list of all VMR patches so that they can be reverted before repo changes can be applied.
    /// 
    /// One exception is when the updated mapping is the one that the VMR patches come from into the VMR (e.g. dotnet/installer).
    /// In this case, we also check which VMR patches are modified by the change and we also returns those.
    /// Examples:
    ///   - An aspnetcore VMR patch is removed from installer - we must remove it from the files it is applied to in the VMR.
    ///   - A new version of patch is synchronized from installer - we must remove the old version and apply the new.
    /// </summary>
    /// <param name="patches">Patches of currently synchronized changes</param>
    private async Task<IReadOnlyCollection<VmrIngestionPatch>> GetVmrPatches(
        IReadOnlyCollection<VmrIngestionPatch> patches,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting a list of VMR patches to restore before we ingest new changes...");

        // Always restore all patches
        var patchesToRestore = new List<VmrIngestionPatch>();

        // If we are not updating the mapping that the VMR patches come from, we're done
        if (_vmrInfo.PatchesPath == null)
        {
            return patchesToRestore;
        }

        patchesToRestore.AddRange(_patchHandler.GetVmrPatches());

        _logger.LogInformation("Checking which VMR patches have changes...");

        // Check which files are modified by every of the patches that bring new changes into the VMR
        foreach (var patch in patches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyCollection<UnixPath> patchedFiles = await _patchHandler.GetPatchedFiles(patch.Path, cancellationToken);
            IEnumerable<LocalPath> affectedPatches = patchedFiles
                .Select(path => patch.ApplicationPath != null ? patch.ApplicationPath! / path : path)
                .Where(path => path.Path.StartsWith(_vmrInfo.PatchesPath) && path.Path.EndsWith(".patch"))
                .Select(path => _vmrInfo.VmrPath / path);

            foreach (LocalPath affectedPatch in affectedPatches)
            {
                // patch is in the folder named as the mapping for which it is applied
                var affectedRepo = affectedPatch.Path.Split(_fileSystem.DirectorySeparatorChar)[^2];
                var affectedMapping = _dependencyTracker.GetMapping(affectedRepo);

                _logger.LogInformation("Detected a change of a VMR patch {patch} for {repo}", affectedPatch, affectedRepo);
                patchesToRestore.Add(new VmrIngestionPatch(affectedPatch, affectedMapping));
            }
        }

        return [..patchesToRestore.DistinctBy(patch => patch.Path)];
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
            await _dependencyTracker.RefreshMetadata(tempFile);
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
