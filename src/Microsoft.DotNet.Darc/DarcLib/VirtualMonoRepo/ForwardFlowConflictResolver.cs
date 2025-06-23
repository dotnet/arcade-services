// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Common;
using NuGet.Versioning;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IForwardFlowConflictResolver
{
    /// <summary>
    /// Tries to resolve well-known conflicts that can occur during a code flow operation.
    /// The conflicts can happen when backward a forward flow PRs get merged out of order.
    /// This can be shown on the following schema (the order of events is numbered):
    /// 
    ///     repo                   VMR
    ///       O────────────────────►O
    ///       │  2.                 │ 1.
    ///       │   O◄────────────────O- - ┐
    ///       │   │            4.   │
    ///     3.O───┼────────────►O   │    │
    ///       │   │             │   │
    ///       │ ┌─┘             │   │    │
    ///       │ │               │   │
    ///     5.O◄┘               └──►O 6. │
    ///       │                 7.  │    O (actual branch for 7. is based on top of 1.)
    ///       |────────────────►O   │
    ///       │                 └──►O 8.
    ///       │                     │
    ///
    /// The conflict arises in step 8. and is caused by the fact that:
    ///   - When the forward flow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
    ///   - This means that the PR branch will be based on 1. (the real PR branch is the "actual 7.")
    ///   - This means that when 6. merged, VMR's source-manifest.json got updated with the SHA of the 3.
    ///   - So the source-manifest in 6. contains the SHA of 3.
    ///   - The forward flow PR branch contains the SHA of 5.
    ///   - So the source-manifest file conflicts on the SHA (3. vs 5.)
    ///   - There's also a similar conflict in the git-info files.
    ///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
    ///   - This is because basically we know we want to set the version files to point at 5.
    /// </summary>
    /// <returns>Conflicted files (if any)</returns>
    Task<IReadOnlyCollection<UnixPath>> TryMergingBranch(
        string mappingName,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        string targetBranch,
        string branchToMerge,
        ForwardFlow currentFlow,
        Codeflow? recentFlow,
        CancellationToken cancellationToken);
}

public class ForwardFlowConflictResolver : IForwardFlowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly ILocalLibGit2Client _libGit2Client;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IAssetLocationResolver _assetLocationResolver;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<BackflowConflictResolver> _logger;

    public ForwardFlowConflictResolver(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        ILocalLibGit2Client libGit2Client,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IAssetLocationResolver assetLocationResolver,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IDependencyFileManager dependencyFileManager,
        IVmrPatchHandler patchHandler,
        IFileSystem fileSystem,
        ILogger<BackflowConflictResolver> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _libGit2Client = libGit2Client;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _assetLocationResolver = assetLocationResolver;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _dependencyFileManager = dependencyFileManager;
        _patchHandler = patchHandler;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<UnixPath>> TryMergingBranch(
        string mappingName,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        string targetBranch,
        string branchToMerge,
        ForwardFlow currentFlow,
        Codeflow? recentFlow,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking if target branch {targetBranch} has conflicts with {headBranch}", branchToMerge, targetBranch);

        await vmr.CheckoutAsync(targetBranch);
        var result = await vmr.RunGitCommandAsync(["merge", "--no-commit", "--no-ff", branchToMerge], cancellationToken);
        if (result.Succeeded)
        {
            try
            {
                await vmr.CommitAsync(
                    $"Merge {branchToMerge} into {targetBranch}",
                    allowEmpty: false,
                    cancellationToken: CancellationToken.None);

                _logger.LogInformation("Successfully merged the branch {targetBranch} into {headBranch} in {repoPath}",
                    branchToMerge,
                    targetBranch,
                    vmr.Path);
            }
            catch (Exception e) when (e.Message.Contains("nothing to commit"))
            {
                // Our branch might be fast-forward and so no commit is needed
                _logger.LogInformation("Branch {targetBranch} had no updates since it was last merged into {headBranch}",
                    branchToMerge,
                    targetBranch);
            }

            return [];
        }
        else
        {
            result = await vmr.RunGitCommandAsync(["diff", "--name-only", "--diff-filter=U", "--relative"], cancellationToken);
            if (!result.Succeeded)
            {
                var abort = await vmr.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
                abort.ThrowIfFailed("Failed to abort a merge when resolving version file conflicts");
                result.ThrowIfFailed("Failed to resolve version file conflicts - failed to get a list of conflicted files");
                throw new InvalidOperationException(); // the line above will throw, including more details
            }

            var conflictedFiles = result
                .GetOutput()
                .Select(line => new UnixPath(line.Trim()))
                .ToList();

            if (!await TryResolveConflicts(
                mappingName,
                vmr,
                sourceRepo,
                conflictedFiles,
                currentFlow,
                recentFlow,
                cancellationToken))
            {
                return conflictedFiles;
            }

            _logger.LogInformation("Successfully resolved file conflicts between branches {targetBranch} and {headBranch}",
                branchToMerge,
                targetBranch);

            try
            {
                await vmr.CommitAsync(
                    $"Merge branch {branchToMerge} into {targetBranch}",
                    allowEmpty: true,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception e) when (e.Message.Contains("Your branch is ahead of"))
            {
                // There was no reason to merge, we're fast-forward ahead from the target branch
            }

            return [];
        }
    }

    private async Task<bool> TryResolveConflicts(
        string mappingName,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        IEnumerable<UnixPath> conflictedFiles,
        ForwardFlow currentFlow,
        Codeflow? recentFlow,
        CancellationToken cancellationToken)
    {
        UnixPath[] allowedConflicts =
        [
            // git-info for the repo
            new UnixPath($"{VmrInfo.GitInfoSourcesDir}/{mappingName}.props"),

            // TODO https://github.com/dotnet/arcade-services/issues/4792: Do not ignore conflicts in version files
            ..DependencyFileManager.DependencyFiles
                .Select(f => new UnixPath(VmrInfo.GetRelativeRepoSourcesPath(mappingName) / f)),
        ];

        foreach (var filePath in conflictedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await TryResolvingConflict(
                    mappingName,
                    vmr,
                    sourceRepo,
                    filePath,
                    allowedConflicts,
                    currentFlow,
                    recentFlow,
                    cancellationToken))
                {
                    continue;
                }
                else
                {
                    _logger.LogInformation("Conflict in {filePath} cannot be resolved automatically", filePath);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to resolve conflicts in {filePath}", filePath);
            }

            await vmr.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
            return false;
        }

        return true;
    }

    private async Task<bool> TryResolvingConflict(
        string mappingName,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        string filePath,
        UnixPath[] allowedConflicts,
        ForwardFlow currentFlow,
        Codeflow? recentFlow,
        CancellationToken cancellationToken)
    {
        // Known conflict in source-manifest.json
        if (string.Equals(filePath, VmrInfo.DefaultRelativeSourceManifestPath, StringComparison.OrdinalIgnoreCase))
        {
            await TryResolvingSourceManifestConflict(vmr, mappingName!, cancellationToken);
            return true;
        }

        // Known conflicts are resolved to "ours" version (the PR version)
        if (allowedConflicts.Any(allowed => filePath.Equals(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Auto-resolving conflict in {file} using PR version", filePath);
            await vmr.ResolveConflict(filePath, ours: true);
            return true;
        }

        UnixPath vmrSourcesPath = VmrInfo.GetRelativeRepoSourcesPath(mappingName);
        if (!filePath.StartsWith(vmrSourcesPath + '/'))
        {
            _logger.LogInformation("Conflict in {file} is not in the source repo, skipping auto-resolution", filePath);
            return false;
        }

        // Unknown conflict, but can be conflicting with a out-of-order recent flow
        // Check DetectRecentFlow documentation for more details
        if (recentFlow != null)
        {
            // If a recent flow is detected, we can try to figure out if the changes that happened to it in the repo
            // apply on top of the VMR file.
            _logger.LogInformation("Trying to auto-resolve a conflict in {filePath} based on a recent flow...", filePath);

            await vmr.ResolveConflict(filePath, ours: false);

            var patchName = _vmrInfo.TmpPath / $"{mappingName}-{Guid.NewGuid()}.patch";
            List<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
                patchName,
                recentFlow.RepoSha,
                currentFlow.RepoSha,
                new UnixPath(filePath.Substring(vmrSourcesPath.Length + 1)),
                filters: null,
                relativePaths: true,
                workingDir: sourceRepo.Path,
                applicationPath: vmrSourcesPath,
                ignoreLineEndings: true,
                cancellationToken);

            if (patches.Count > 1)
            {
                foreach (var patch in patches)
                {
                    try
                    {
                        _fileSystem.DeleteFile(patch.Path);
                    }
                    catch
                    {
                    }
                }

                throw new InvalidOperationException("Cannot auto-resolve conflicts for files over 1GB in size");
            }

            try
            {
                await _patchHandler.ApplyPatch(
                    patches[0],
                    vmr.Path,
                    removePatchAfter: true,
                    reverseApply: false,
                    cancellationToken);
                _logger.LogInformation("Successfully auto-resolved a conflict in {filePath} based on a recent flow", filePath);
                return true;
            }
            catch (PatchApplicationFailedException)
            {
                // If the patch failed, we cannot resolve the conflict automatically
                // We will just leave it as is and let the user resolve it manually
                _logger.LogInformation("Failed to auto-resolve conflicts in {filePath} - conflicting changes detected", filePath);
                return false;
            }
        }

        return false;
    }

    // TODO https://github.com/dotnet/arcade-services/issues/3378: This might not work for batched subscriptions
    private async Task TryResolvingSourceManifestConflict(ILocalGitRepo vmr, string mappingName, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Auto-resolving conflict in {file}", VmrInfo.DefaultRelativeSourceManifestPath);

        // We load the source manifest from the target branch and replace the
        // current mapping (and its submodules) with our branches' information
        var result = await vmr.RunGitCommandAsync(
            ["show", "MERGE_HEAD:" + VmrInfo.DefaultRelativeSourceManifestPath],
            cancellationToken);

        var theirSourceManifest = SourceManifest.FromJson(result.StandardOutput);
        var ourSourceManifest = _sourceManifest;
        var updatedMapping = ourSourceManifest.Repositories.First(r => r.Path == mappingName);

        theirSourceManifest.UpdateVersion(
            mappingName,
            updatedMapping.RemoteUri,
            updatedMapping.CommitSha,
            updatedMapping.PackageVersion,
            updatedMapping.BarId);

        var theirAffectedSubmodules = theirSourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mappingName + "/"))
            .ToList();
        foreach (var submodule in theirAffectedSubmodules)
        {
            theirSourceManifest.RemoveSubmodule(submodule);
        }

        var ourAffectedSubmodules = ourSourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mappingName + "/"))
            .ToList();
        foreach (var submodule in ourAffectedSubmodules)
        {
            theirSourceManifest.UpdateSubmodule(submodule);
        }

        _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, theirSourceManifest.ToJson());
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);
        await vmr.StageAsync([_vmrInfo.SourceManifestPath], cancellationToken);
    }

    public async Task<VersionFileUpdateResult> TryMergingBackflowBranchAndUpdateDependencies(
        SourceMapping mapping,
        Codeflow lastFlow,
        Backflow currentFlow,
        ILocalGitRepo targetRepo,
        Build build,
        string headBranch,
        string targetBranch,
        IReadOnlyCollection<string>? excludedAssets,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var mergeSuccessful = await TryMergingBranch(
            targetRepo,
            headBranch,
            targetBranch,
            cancellationToken);

        var excludedAssetsMatcher = GetExcludedAssetsMatcher(excludedAssets);

        if (mergeSuccessful)
        {
            try
            {
                var updates = await BackflowDependenciesAndToolset(
                    mapping.Name,
                    targetRepo,
                    targetBranch,
                    build,
                    excludedAssetsMatcher,
                    lastFlow,
                    currentFlow,
                    cancellationToken);
                return new VersionFileUpdateResult(ConflictedFiles: [], updates);
            }
            catch (Exception e)
            {
                // We don't want to push this as there is some problem
                _logger.LogError(e, "Failed to update dependencies after merging {targetBranch} into {headBranch} in {repoPath}",
                    targetBranch,
                    headBranch,
                    targetRepo.Path);
                throw;
            }
        }

        return await ResolveVersionFileConflicts(
            mapping,
            lastFlow,
            currentFlow,
            targetRepo,
            build,
            headBranch,
            targetBranch,
            excludedAssetsMatcher,
            headBranchExisted,
            cancellationToken);
    }

    private async Task<bool> TryMergingBranch(
        ILocalGitRepo repo,
        string headBranch,
        string branchToMerge,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking if target branch {targetBranch} has conflicts with {headBranch}", branchToMerge, headBranch);

        await repo.CheckoutAsync(headBranch);
        var result = await repo.RunGitCommandAsync(["merge", "--no-commit", "--no-ff", branchToMerge], cancellationToken);
        if (!result.Succeeded)
        {
            return false;
        }

        try
        {
            await repo.CommitAsync(
                $"Merging {branchToMerge} into {headBranch}",
                allowEmpty: false,
                cancellationToken: CancellationToken.None);

            _logger.LogInformation("Successfully merged the branch {targetBranch} into {headBranch} in {repoPath}",
                branchToMerge,
                headBranch,
                repo.Path);
        }
        catch (Exception e) when (e.Message.Contains("nothing to commit"))
        {
            // Our branch might be fast-forward and so no commit was needed
        }

        return true;
    }

    /// <summary>
    /// Tries to resolve well-known conflicts that can occur during a code flow operation.
    /// The conflicts can happen when backward a forward flow PRs get merged out of order.
    /// This can be shown on the following schema (the order of events is numbered):
    /// 
    ///     repo                   VMR
    ///       O────────────────────►O
    ///       │  2.                 │ 1.
    ///       │   O◄────────────────O- - ┐
    ///       │   │            4.   │
    ///     3.O───┼────────────►O   │    │
    ///       │   │             │   │
    ///       │ ┌─┘             │   │    │
    ///       │ │               │   │
    ///     5.O◄┘               └──►O 6. │
    ///       │                 7.  │    O (actual branch for 7. is based on top of 1.)
    ///       |────────────────►O   │
    ///       │                 └──►O 8.
    ///       │                     │
    ///
    /// The conflict arises in step 8. and is caused by the fact that:
    ///   - When the forward flow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
    ///   - This means that the PR branch will be based on 1. (the real PR branch is the "actual 7.")
    ///   - This means that when 6. merged, VMR's source-manifest.json got updated with the SHA of the 3.
    ///   - So the source-manifest in 6. contains the SHA of 3.
    ///   - The forward flow PR branch contains the SHA of 5.
    ///   - So the source-manifest file conflicts on the SHA (3. vs 5.)
    ///   - There's also a similar conflict in the git-info files.
    ///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
    ///   - This is because basically we know we want to set the version files to point at 5.
    /// </summary>
    private async Task<VersionFileUpdateResult> ResolveVersionFileConflicts(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        Build build,
        string headBranch,
        string branchToMerge,
        Matcher? excludedAssetsMatcher,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        async Task AbortMerge()
        {
            var result = await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
            result.ThrowIfFailed("Failed to abort a merge when resolving version file conflicts");
        }

        // When we had conflicts, we verify that they can be resolved (i.e. they are only in version files)
        var result = await repo.RunGitCommandAsync(["diff", "--name-only", "--diff-filter=U", "--relative"], cancellationToken);
        if (!result.Succeeded)
        {
            await AbortMerge();
            result.ThrowIfFailed("Failed to resolve version file conflicts - failed to get a list of conflicted files");
        }

        var conflictedFiles = result.GetOutput();

        var unresolvableConflicts = conflictedFiles
            .Except(DependencyFileManager.DependencyFiles)
            .ToList();

        if (unresolvableConflicts.Count > 0)
        {
            _logger.LogInformation("Failed to merge the branch {targetBranch} into {headBranch} due to unresolvable conflicts: {conflicts}",
                branchToMerge,
                headBranch,
                string.Join(", ", unresolvableConflicts));

            await AbortMerge();

            // If this is a first commit, we need to update dependencies still
            if (!headBranchExisted)
            {
                var updates = await BackflowDependenciesAndToolset(
                    mapping.Name,
                    repo,
                    branchToMerge,
                    build,
                    excludedAssetsMatcher,
                    lastFlow,
                    (Backflow)currentFlow,
                    cancellationToken);
                return new VersionFileUpdateResult(ConflictedFiles: [.. conflictedFiles.Select(file => new UnixPath(file))], updates);
            }

            return new VersionFileUpdateResult([.. conflictedFiles.Select(file => new UnixPath(file))], []);
        }

        foreach (var file in conflictedFiles)
        {
            // Revert files so that we can resolve the conflicts
            // We use the target branch version when we are flowing the first time (because we did not flow the version files yet)
            // We use the head branch version when we are flowing again because it already has updates from previous flow
            // plus it can contain additional changes from the PR
            await repo.RunGitCommandAsync(["checkout", headBranchExisted ? "--ours" : "--theirs", file], cancellationToken);
        }

        try
        {
            // When only version files are conflicted, we can resolve the conflicts by generating them correctly
            var updates = await BackflowDependenciesAndToolset(
                mapping.Name,
                repo,
                branchToMerge,
                build,
                excludedAssetsMatcher,
                lastFlow,
                (Backflow)currentFlow,
                cancellationToken);
            return new VersionFileUpdateResult(ConflictedFiles: [], updates);
        }
        catch (ConflictingDependencyUpdateException e)
        {
            _logger.LogInformation("Detected conflicts in version file changes - failed to update dependencies: {message}", e.Message);
            await AbortMerge();
            return new VersionFileUpdateResult([.. conflictedFiles.Select(file => new UnixPath(file))], []);
        }
        catch
        {
            // We don't want to push this as there is some problem
            _logger.LogError("Failed to update dependencies after merging {targetBranch} into {headBranch} in {repoPath}",
                branchToMerge,
                headBranch,
                repo.Path);
            throw;
        }
    }

    /// <summary>
    /// Updates version details, eng/common and other version files (global.json, ...) based on a build that is being flown.
    /// </summary>
    /// <returns>List of dependency changes</returns>
    private async Task<List<DependencyUpdate>> BackflowDependenciesAndToolset(
        string mappingName,
        ILocalGitRepo targetRepo,
        string targetBranch,
        Build build,
        Matcher? excludedAssetsMatcher,
        Codeflow lastFlow,
        Backflow currentFlow,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Resolving backflow dependency updates between VMR {vmrSha1}..{vmrSha2} and {repo} {repoSha1}..{repoSha2}",
            lastFlow.VmrSha,
            Constants.HEAD,
            mappingName,
            lastFlow.RepoSha,
            targetBranch);

        var previousRepoDependencies = await GetRepoDependencies(targetRepo, lastFlow.RepoSha);
        var currentRepoDependencies = await GetRepoDependencies(targetRepo, targetBranch);

        // Similarly to the code flow algorithm, we compare the corresponding commits
        // and the contents of the version files inside.
        // We distinguish the direction of the previous flow vs the current flow.
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        var previousVmrDependencies = lastFlow is Backflow
            ? await GetVmrDependencies(vmr, mappingName, lastFlow.VmrSha)
            : previousRepoDependencies;
        var currentVmrDependencies = await GetVmrDependencies(vmr, mappingName, currentFlow.VmrSha);

        List<DependencyUpdate> repoChanges = ComputeChanges(
            excludedAssetsMatcher,
            previousRepoDependencies,
            currentRepoDependencies);

        List<DependencyUpdate> vmrChanges = ComputeChanges(
            excludedAssetsMatcher,
            previousVmrDependencies,
            currentVmrDependencies);

        List<AssetData> buildAssets = build.Assets
            .Where(a => !IsExcludedAsset(a.Name, excludedAssetsMatcher))
            .Select(a => new AssetData(a.NonShipping)
            {
                Name = a.Name,
                Version = a.Version
            })
            .ToList();

        // All packages that can appear in current dependency update
        var headBranchDependencies = await GetRepoDependencies(targetRepo, commit: null! /* working tree */);
        var uniquePackages = headBranchDependencies.Dependencies
            .Select(dep => dep.Name)
            .Concat(vmrChanges.Select(c => c.From?.Name ?? c.To.Name))
            .Concat(repoChanges.Select(c => c.From?.Name ?? c.To.Name))
            .Distinct()
            .Where(dep => !IsExcludedAsset(dep, excludedAssetsMatcher))
            .ToHashSet();

        var versionUpdates = new List<DependencyDetail>();
        var buildUpdates = new List<AssetData>();
        var removals = new HashSet<string>();
        var additions = new List<DependencyDetail>();

        foreach (var assetName in uniquePackages)
        {
            AssetData? buildAsset = buildAssets.FirstOrDefault(a => a.Name == assetName);
            DependencyUpdate? repoChange = repoChanges.FirstOrDefault(c => assetName == (c.From?.Name ?? c.To!.Name));
            DependencyUpdate? vmrChange = vmrChanges.FirstOrDefault(c => assetName == (c.From?.Name ?? c.To!.Name));

            bool repoAddition = repoChange != null && repoChange.From == null;
            bool vmrAddition = vmrChange != null && vmrChange.From == null;
            bool repoRemoval = repoChange != null && repoChange.To == null;
            bool vmrRemoval = vmrChange != null && vmrChange.To == null;
            bool includedInBuild = buildAsset != null;
            bool repoUpdated = repoChange?.From != null && repoChange.To != null;
            bool vmrUpdated = vmrChange?.From != null && vmrChange.To != null;

            DependencyDetail? repoVersion = repoChange?.To;
            DependencyDetail? vmrVersion = vmrChange?.To;

            // When part of the build, we use the version from the build
            // This is the most common case
            if (includedInBuild)
            {
                _logger.LogInformation("Asset {assetName} contained in build, updating to {version}", assetName, buildAsset!.Version);

                buildUpdates.Add(new AssetData(false)
                {
                    Name = assetName,
                    Version = buildAsset.Version,
                });

                continue;
            }

            if (repoUpdated && vmrUpdated)
            {
                if (SemanticVersion.TryParse(repoVersion!.Version, out var repoSemVer) && SemanticVersion.TryParse(vmrVersion!.Version, out var vmrSemVer))
                {
                    DependencyDetail newerVersion = repoSemVer > vmrSemVer ? repoVersion! : vmrVersion!;
                    _logger.LogInformation(
                        "Asset {assetName} updated to {repoVersion} in the repo and {vmrVersion} in the VMR. Choosing the newer version {newerVersion}",
                        assetName,
                        repoVersion.Version,
                        vmrVersion.Version,
                        newerVersion.Version);
                    versionUpdates.Add(newerVersion);
                    continue;
                }

                // We can't tell which one is newer, we pick the repo version
                _logger.LogInformation(
                    "Asset {assetName} updated to {repoVersion} in the repo and {vmrVersion} in the VMR. Choosing the repo version {repoVersion}",
                    assetName,
                    repoVersion.Version,
                    vmrVersion!.Version,
                    repoVersion.Version);
                versionUpdates.Add(repoVersion);
                continue;
            }

            if (repoRemoval)
            {
                if (vmrAddition)
                {
                    // Asset was removed from the repo and added to the VMR at the same time
                    _logger.LogInformation(
                        "Asset {assetName} was removed from the repo and added to the VMR at the same time. Skipping the asset",
                        assetName);
                    throw new ConflictingDependencyUpdateException(repoChange!, vmrChange!);
                }

                // Asset got removed from the repo, we can skip it (it won't be in the target V.D.xml)
                _logger.LogInformation("Asset {assetName} was removed from the repo", assetName);
                continue;
            }

            if (vmrRemoval)
            {
                if (repoAddition)
                {
                    // Asset was removed from the VMR and added to the repo at the same time
                    _logger.LogInformation(
                        "Asset {assetName} was removed from the VMR and added to the repo at the same time. Skipping the asset",
                        assetName);
                    throw new ConflictingDependencyUpdateException(repoChange!, vmrChange!);
                }

                // Asset got removed from the VMR, we need to remove it
                removals.Add(assetName);
                _logger.LogInformation("Asset {assetName} was removed from the VMR, removing from repo too", assetName);
                continue;
            }

            if (repoAddition || vmrAddition)
            {
                if (repoAddition && vmrAddition && SemanticVersion.TryParse(repoVersion!.Version, out var repoSemVer) && SemanticVersion.TryParse(vmrVersion!.Version, out var vmrSemVer))
                {
                    DependencyDetail newerVersion = repoSemVer > vmrSemVer ? repoVersion! : vmrVersion!;
                    _logger.LogInformation(
                        "Asset {assetName} added in both the repo ({repoVersion}) and the VMR ({vmrVersion}). Choosing the newer version {newerVersion}",
                        assetName,
                        repoVersion.Version,
                        vmrVersion.Version,
                        newerVersion.Version);
                    additions.Add(newerVersion);
                    continue;
                }

                if (repoAddition)
                {
                    // Asset was added to the repo, no change necessary
                    _logger.LogInformation("Asset {assetName} was added in the repo", assetName);
                    additions.Add(repoVersion!);
                    continue;
                }

                if (vmrAddition)
                {
                    // Asset was added to the VMR, we need to add it
                    _logger.LogInformation("Asset {assetName} version {version} was added in the VMR, adding it to the repo too", assetName, vmrVersion!.Version);
                    additions.Add(vmrVersion);
                    continue;
                }
            }

            if (repoUpdated)
            {
                // Don't do anything, package is updated in the repo already
                _logger.LogInformation("Asset {assetName} updated to {version} in the repo, skipping", assetName, repoVersion!.Version);
                continue;
            }

            if (vmrUpdated)
            {
                // Package got updated in the VMR only
                _logger.LogInformation("Asset {assetName} updated to {version} in the VMR, updating in the repo too", assetName, vmrVersion!.Version);
                versionUpdates.Add(vmrVersion);
                continue;
            }

            _logger.LogDebug("Asset {assetName} is not part of the build, not updated in the repo and not updated in the VMR. Skipping", assetName);
        }

        foreach (var removedAsset in removals)
        {
            await _dependencyFileManager.RemoveDependencyAsync(removedAsset, targetRepo.Path, null!);
        }

        foreach (var addedDependency in additions)
        {
            await _dependencyFileManager.AddDependencyAsync(addedDependency, targetRepo.Path, branch: null!);
        }

        currentRepoDependencies = await GetRepoDependencies(targetRepo, null!);

        List<DependencyDetail> updates = _coherencyUpdateResolver
            .GetRequiredNonCoherencyUpdates(
                build.GetRepository(),
                build.Commit,
                buildUpdates,
                currentRepoDependencies.Dependencies)
            .Select(u => u.To)
            .Concat(versionUpdates)
            .ToList();

        await _assetLocationResolver.AddAssetLocationToDependenciesAsync(updates);

        // We add all of the packages since it's a harmless operation just to be sure they made through all the merging
        // Later we update them
        foreach (var update in updates)
        {
            await _dependencyFileManager.AddDependencyAsync(new DependencyDetail(update), targetRepo.Path, branch: null!);
        }

        // If we are updating the arcade sdk we need to update the eng/common files as well
        DependencyDetail? arcadeItem = updates.GetArcadeUpdate();

        SemanticVersion? targetDotNetVersion = null;

        // The arcade backflow subscriptions has all assets excluded, but we want to update the global.json sdk version anyway
        if (arcadeItem != null || mappingName == "arcade")
        {
            // Even tho we are backflowing from the VMR, we want to get the sdk version from VMR`s global.json, not src/arcade's
            targetDotNetVersion = await _dependencyFileManager.ReadToolsDotnetVersionAsync(build.GetRepository(), build.Commit, repoIsVmr: false);
        }

        GitFileContentContainer updatedFiles = await _dependencyFileManager.UpdateDependencyFiles(
            updates,
            new SourceDependency(build, mappingName),
            targetRepo.Path,
            branch: null, // reads the working tree
            currentRepoDependencies.Dependencies,
            targetDotNetVersion,
            // make sure that we always backflow the VMRs root global.json sdk version
            forceGlobalJsonUpdate: true);

        // This actually does not commit but stages only
        await _libGit2Client.CommitFilesAsync(updatedFiles.GetFilesToCommit(), targetRepo.Path, null, null);

        // Update eng/common files
        if (arcadeItem != null)
        {
            // Check if the VMR contains src/arcade/eng/common
            var arcadeEngCommonDir = _vmrInfo.VmrPath / VmrInfo.ArcadeRepoDir / Constants.CommonScriptFilesPath;
            if (!_fileSystem.DirectoryExists(arcadeEngCommonDir))
            {
                _logger.LogWarning($"VMR does not contain {VmrInfo.ArcadeRepoDir / Constants.CommonScriptFilesPath}. Skipping the common scripts update");
            }
            else
            {
                _logger.LogInformation("Updating eng/common files from the VMR's {path}", arcadeEngCommonDir);

                var commonDir = targetRepo.Path / Constants.CommonScriptFilesPath;
                if (_fileSystem.DirectoryExists(commonDir))
                {
                    _fileSystem.DeleteDirectory(commonDir, true);
                }

                _fileSystem.CopyDirectory(
                    arcadeEngCommonDir,
                    targetRepo.Path / Constants.CommonScriptFilesPath,
                    true);
            }
        }

        if (!await targetRepo.HasWorkingTreeChangesAsync())
        {
            _logger.LogInformation("No changes to dependencies in this backflow update");
            return [];
        }

        await targetRepo.StageAsync(["."], cancellationToken);

        List<DependencyUpdate> dependencyUpdates = [
            ..additions
                .Where(addition => headBranchDependencies.Dependencies.All(a => addition.Name != a.Name))
                .Select(addition => new DependencyUpdate()
                {
                    From = null,
                    To = addition
                }),
            ..removals
                .Where(addition => headBranchDependencies.Dependencies.Any(a => addition == a.Name))
                .Select(removal => new DependencyUpdate()
                {
                    From = headBranchDependencies.Dependencies.First(d => d.Name == removal),
                    To = null
                }),
            ..updates
                .Select(update => new DependencyUpdate()
                {
                    From = headBranchDependencies.Dependencies.Concat(additions).First(d => d.Name == update.Name),
                    To = update,
                }),
        ];

        string commitMessage = string.Concat(
            $"Update dependencies from {build.GetRepository()} build {build.Id}",
            Environment.NewLine,
            BuildDependencyUpdateCommitMessage(dependencyUpdates));

        await targetRepo.CommitAsync(
            commitMessage,
            allowEmpty: false,
            cancellationToken: cancellationToken);

        return dependencyUpdates;
    }

    private static Matcher? GetExcludedAssetsMatcher(IReadOnlyCollection<string>? excludedAssets)
    {
        if (excludedAssets == null || excludedAssets.Count == 0)
        {
            return null;
        }
        var matcher = new Matcher();
        matcher.AddIncludePatterns(excludedAssets);
        return matcher;
    }

    private static bool IsExcludedAsset(string asset, Matcher? excludedAssetsMatcher)
    {
        if (excludedAssetsMatcher == null)
        {
            return false;
        }

        return excludedAssetsMatcher.Match(asset).HasMatches;
    }

    private static List<DependencyUpdate> ComputeChanges(Matcher? excludedAssetsMatcher, VersionDetails before, VersionDetails after)
    {
        var dependencyChanges = before.Dependencies
            .Where(dep => !IsExcludedAsset(dep.Name, excludedAssetsMatcher))
            .Select(dep => new DependencyUpdate()
            {
                From = dep,
            })
            .ToList();

        // Pair dependencies with the same name
        foreach (var dep in after.Dependencies.Where(dep => !IsExcludedAsset(dep.Name, excludedAssetsMatcher)))
        {
            var existing = dependencyChanges.FirstOrDefault(d => d.From?.Name == dep.Name);
            if (existing != null)
            {
                existing.To = dep;
            }
            else
            {
                dependencyChanges.Add(new DependencyUpdate()
                {
                    From = null,
                    To = dep,
                });
            }
        }

        // Check if there are any actual changes
        return dependencyChanges
            .Where(change => change.From?.Version != change.To?.Version
                || change.From?.Commit != change.To?.Commit
                || change.From?.RepoUri != change.To?.RepoUri)
            .ToList();
    }

    public static string BuildDependencyUpdateCommitMessage(IEnumerable<DependencyUpdate> updates)
    {
        if (!updates.Any())
        {
            return "No dependency updates to commit";
        }
        Dictionary<string, List<string>> removedDependencies = new();
        Dictionary<string, List<string>> addedDependencies = new();
        Dictionary<string, List<string>> updatedDependencies = new();
        foreach (DependencyUpdate dependencyUpdate in updates)
        {
            if (dependencyUpdate.To != null && dependencyUpdate.From == null)
            {
                string versionBlurb = $"Version {dependencyUpdate.To.Version}";
                AddDependencyToDictionary(addedDependencies, versionBlurb, dependencyUpdate.To.Name);
                continue;
            }
            if (dependencyUpdate.To == null && dependencyUpdate.From != null)
            {
                string versionBlurb = $"Version {dependencyUpdate.From.Version}";
                AddDependencyToDictionary(removedDependencies, versionBlurb, dependencyUpdate.From.Name);
                continue;
            }
            if (dependencyUpdate.To != null && dependencyUpdate.From != null)
            {
                string versionBlurb = $"Version {dependencyUpdate.From.Version} -> {dependencyUpdate.To.Version}";
                AddDependencyToDictionary(updatedDependencies, versionBlurb, dependencyUpdate.From.Name);
                continue;
            }
        }

        var result = new StringBuilder();
        if (updatedDependencies.Any())
        {
            result.AppendLine("Updated Dependencies:");
            foreach ((string versionBlurb, List<string> packageNames) in updatedDependencies)
            {
                result.AppendLine(string.Join(", ", packageNames) + $" ({versionBlurb})");
            }
            result.AppendLine();
        }
        if (addedDependencies.Any())
        {
            result.AppendLine("Added Dependencies:");
            foreach ((string versionBlurb, List<string> packageNames) in addedDependencies)
            {
                result.AppendLine(string.Join(", ", packageNames) + $" ({versionBlurb})");
            }
            result.AppendLine();
        }
        if (removedDependencies.Any())
        {
            result.AppendLine("Removed Dependencies:");
            foreach ((string versionBlurb, List<string> packageNames) in removedDependencies)
            {
                result.AppendLine(string.Join(", ", packageNames) + $" ({versionBlurb})");
            }
            result.AppendLine();
        }
        return result.ToString().TrimEnd();
    }

    private static void AddDependencyToDictionary(Dictionary<string, List<string>> dictionary, string versionBlurb, string dependencyName)
    {
        if (dictionary.TryGetValue(versionBlurb, out var list))
        {
            list.Add(dependencyName);
        }
        else
        {
            dictionary[versionBlurb] = new List<string> { dependencyName };
        }
    }

    private async Task<VersionDetails> GetRepoDependencies(ILocalGitRepo repo, string commit)
        => GetDependencies(await repo.GetFileFromGitAsync(VersionFiles.VersionDetailsXml, commit));

    private async Task<VersionDetails> GetVmrDependencies(ILocalGitRepo vmr, string mapping, string commit)
        => GetDependencies(await vmr.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.VersionDetailsXml, commit));

    private VersionDetails GetDependencies(string? content)
        => content == null
            ? new VersionDetails([], null)
            : _versionDetailsParser.ParseVersionDetailsXml(content, includePinned: false);
}
