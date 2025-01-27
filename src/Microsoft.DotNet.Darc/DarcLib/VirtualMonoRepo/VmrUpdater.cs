// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is able to update an individual repository within the VMR from one commit to another.
/// It creates git diffs while adhering to cloaking rules, accommodating for patched files, resolving submodules.
/// It can also update other repositories recursively based on the dependencies stored in Version.Details.xml.
/// This implementation is meant to be used in the pre-.NET 10 VMR synchronization process
/// (one-way synchronization from dotnet/sdk using a pipeline).
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
    private readonly IBasicBarClient _barClient;
    private readonly ILogger<VmrUpdater> _logger;
    private readonly ISourceManifest _sourceManifest;
    private readonly IThirdPartyNoticesGenerator _thirdPartyNoticesGenerator;
    private readonly ILocalGitClient _localGitClient;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly IWorkBranchFactory _workBranchFactory;

    public VmrUpdater(
        IVmrDependencyTracker dependencyTracker,
        IVersionDetailsParser versionDetailsParser,
        IRepositoryCloneManager cloneManager,
        IVmrPatchHandler patchHandler,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        ICodeownersGenerator codeownersGenerator,
        ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IDependencyFileManager dependencyFileManager,
        IGitRepoFactory gitRepoFactory,
        IWorkBranchFactory workBranchFactory,
        IFileSystem fileSystem,
        IBasicBarClient barClient,
        ILogger<VmrUpdater> logger,
        ISourceManifest sourceManifest,
        IVmrInfo vmrInfo)
        : base(vmrInfo, sourceManifest, dependencyTracker, patchHandler, versionDetailsParser, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, dependencyFileManager, barClient, fileSystem, logger)
    {
        _logger = logger;
        _sourceManifest = sourceManifest;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _cloneManager = cloneManager;
        _patchHandler = patchHandler;
        _fileSystem = fileSystem;
        _barClient = barClient;
        _thirdPartyNoticesGenerator = thirdPartyNoticesGenerator;
        _localGitClient = localGitClient;
        _gitRepoFactory = gitRepoFactory;
        _workBranchFactory = workBranchFactory;
    }

    public async Task<bool> UpdateRepository(
        string mappingName,
        string? targetRevision,
        bool updateDependencies,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool generateCredScanSuppressions,
        bool discardPatches,
        bool lookUpBuilds,
        bool resetToRemoteWhenCloningRepo = false,
        CancellationToken cancellationToken = default)
    {
        await _dependencyTracker.RefreshMetadata();

        var mapping = _dependencyTracker.GetMapping(mappingName);

        string? officialBuildId = null;
        int? barId = null;
        Build? build = null;

        if (lookUpBuilds)
        {
            build = (await _barClient.GetBuildsAsync(mapping.DefaultRemote, targetRevision))
                .FirstOrDefault();

            officialBuildId = build?.AzureDevOpsBuildNumber;
            barId = build?.Id;
        }

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
            TargetVersion: build?.Assets.FirstOrDefault()?.Version,
            Parent: null,
            officialBuildId,
            barId);

        if (updateDependencies)
        {
            return await UpdateRepositoryRecursively(
                dependencyUpdate,
                additionalRemotes,
                tpnTemplatePath,
                generateCodeowners,
                generateCredScanSuppressions,
                discardPatches,
                lookUpBuilds,
                cancellationToken);
        }
        else
        {
            try
            {
                IReadOnlyCollection<VmrIngestionPatch> patchesToReapply = await UpdateRepositoryInternal(
                    dependencyUpdate,
                    restoreVmrPatches: true,
                    additionalRemotes,
                    tpnTemplatePath,
                    generateCodeowners,
                    generateCredScanSuppressions,
                    discardPatches,
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
    }

    private async Task<IReadOnlyCollection<VmrIngestionPatch>> UpdateRepositoryInternal(
        VmrDependencyUpdate update,
        bool restoreVmrPatches,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool generateCredScanSuppressions,
        bool discardPatches,
        bool resetToRemoteWhenCloningRepo = false,
        CancellationToken cancellationToken = default)
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
                    update.OfficialBuildId,
                    update.BarId,
                    update.Mapping,
                    currentVersion,
                    cancellationToken);
                return [];
            }

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
            additionalRemotes,
            currentVersion.Sha,
            author: null,
            commitMessage,
            restoreVmrPatches,
            tpnTemplatePath,
            generateCodeowners,
            generateCredScanSuppressions,
            discardPatches,
            cancellationToken);
    }

    /// <summary>
    /// Updates a repository and all of it's dependencies recursively starting with a given mapping.
    /// Always updates to the first version found per repository in the dependency tree.
    /// </summary>
    private async Task<bool> UpdateRepositoryRecursively(
        VmrDependencyUpdate rootUpdate,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        string? tpnTemplatePath,
        bool generateCodeowners,
        bool generateCredScanSuppressions,
        bool discardPatches,
        bool lookUpBuilds,
        CancellationToken cancellationToken)
    {
        string originalRootSha = GetCurrentVersion(rootUpdate.Mapping);

        _logger.LogInformation("Recursive update for {repo} / {from}{arrow}{to}",
            rootUpdate.Mapping.Name,
            Commit.GetShortSha(originalRootSha),
            Constants.Arrow,
            rootUpdate.TargetRevision);

        var updates = (await GetAllDependenciesAsync(rootUpdate, additionalRemotes, lookUpBuilds, cancellationToken)).ToList();

        var extraneousMappings = _dependencyTracker.Mappings
            .Where(mapping => !updates.Any(update => update.Mapping == mapping) && !mapping.DisableSynchronization)
            .Select(mapping => mapping.Name);

        if (extraneousMappings.Any())
        {
            var separator = $"{Environment.NewLine}  - ";
            _logger.LogWarning($"The following mappings do not appear in current update's dependency tree:{separator}{{extraMappings}}",
                string.Join(separator, extraneousMappings));
        }

        // Synchronization creates commits (one per mapping and some extra) on a separate branch that is then merged into original one

        var workBranchName = "sync" +
            $"/{rootUpdate.Mapping.Name}" +
            $"/{Commit.GetShortSha(GetCurrentVersion(rootUpdate.Mapping))}-{rootUpdate.TargetRevision}";
        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(GetLocalVmr(), workBranchName);

        // Collection of all affected VMR patches we will need to restore after the sync
        var vmrPatchesToReapply = new List<VmrIngestionPatch>();

        // Dependencies that were already updated during this run
        var updatedDependencies = new HashSet<VmrDependencyUpdate>();

        foreach (VmrDependencyUpdate update in updates)
        {
            if (update.Mapping.DisableSynchronization)
            {
                _logger.LogInformation("Synchronization for {repo} is disabled, skipping...", update.Mapping.Name);
                continue;
            }

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

            IReadOnlyCollection<VmrIngestionPatch> patchesToReapply;
            try
            {
                patchesToReapply = await UpdateRepositoryInternal(
                    update,
                    restoreVmrPatches: update.Parent == null,
                    additionalRemotes,
                    tpnTemplatePath,
                    generateCodeowners,
                    generateCredScanSuppressions,
                    discardPatches,
                    resetToRemoteWhenCloningRepo: false,
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
            catch (Exception)
            {
                _logger.LogWarning(
                    InterruptedSyncExceptionMessage,
                    workBranch.OriginalBranch.StartsWith("sync") || workBranch.OriginalBranch.StartsWith("init")
                        ? "the original"
                        : workBranch.OriginalBranch);
                throw;
            }

            vmrPatchesToReapply.AddRange(patchesToReapply);

            updatedDependencies.Add(update with
            {
                // We resolve the SHA again because original target could have been a branch name
                TargetRevision = GetCurrentVersion(update.Mapping),

                // We also store the original SHA (into the version!) so that later we use it in the commit message
                TargetVersion = currentSha,
            });
        }

        string finalRootSha = GetCurrentVersion(rootUpdate.Mapping);
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

        await ApplyVmrPatches(workBranch, vmrPatchesToReapply, cancellationToken);

        await CleanUpRemovedRepos(tpnTemplatePath);

        var commitMessage = PrepareCommitMessage(
            MergeCommitMessage,
            rootUpdate.Mapping.Name,
            rootUpdate.Mapping.DefaultRemote,
            originalRootSha,
            finalRootSha,
            summaryMessage.ToString());

        await workBranch.MergeBackAsync(commitMessage);

        _logger.LogInformation("Recursive update for {repo} finished.{newLine}{message}",
            rootUpdate.Mapping.Name,
            Environment.NewLine,
            summaryMessage);

        return updatedDependencies.Any();
    }

    private async Task ApplyVmrPatches(IWorkBranch workBranch, List<VmrIngestionPatch> vmrPatchesToReapply, CancellationToken cancellationToken)
    {
        if (!vmrPatchesToReapply.Any())
        {
            return;
        }

        try
        {
            await ReapplyVmrPatchesAsync(vmrPatchesToReapply, cancellationToken);
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

        // TODO: Workaround for cases when we get CRLF problems on Windows
        // We should figure out why restoring and reapplying VMR patches leaves working tree with EOL changes
        // https://github.com/dotnet/arcade-services/issues/3277
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var vmr = GetLocalVmr();
            if (await vmr.HasWorkingTreeChangesAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _localGitClient.CheckoutAsync(_vmrInfo.VmrPath, ".");

                // Sometimes not even checkout helps, so we check again
                if (await vmr.HasWorkingTreeChangesAsync())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _localGitClient.RunGitCommandAsync(
                        _vmrInfo.VmrPath,
                        ["add", "--u", "."],
                        cancellationToken: default);

                    await _localGitClient.RunGitCommandAsync(
                        _vmrInfo.VmrPath,
                        ["commit", "--amend", "--no-edit"],
                        cancellationToken: default);
                }
            }
        }
    }

    /// <summary>
    /// Removes changes applied by VMR patches and restores the original state of the files.
    /// </summary>
    /// <param name="updatedMapping">Mapping that is currently being updated (so we get its patches)</param>
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

    private string GetCurrentVersion(SourceMapping mapping)
    {
        var version = _dependencyTracker.GetDependencyVersion(mapping)
            ?? throw new RepositoryNotInitializedException($"Repository {mapping.Name} has not been initialized yet");

        return version.Sha;
    }

    private async Task CleanUpRemovedRepos(string? tpnTemplatePath)
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

        if (tpnTemplatePath != null)
        {
            await _thirdPartyNoticesGenerator.UpdateThirdPartyNotices(tpnTemplatePath);
        }

        await GetLocalVmr().StageAsync(["*"]);
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
        string? officialBuildId,
        int? barId,
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
            RemoteUri: _sourceManifest.GetRepoVersion(mapping.Name).RemoteUri,
            OfficialBuildId: officialBuildId,
            BarId: barId));

        var filesToAdd = new List<string>
        {
            VmrInfo.GitInfoSourcesDir,
            _vmrInfo.SourceManifestPath
        };

        cancellationToken.ThrowIfCancellationRequested();
        await GetLocalVmr().StageAsync(filesToAdd, cancellationToken);
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
