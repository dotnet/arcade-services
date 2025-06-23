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

public class ForwardFlowConflictResolver : CodeFlowConflictResolver, IForwardFlowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ForwardFlowConflictResolver> _logger;

    public ForwardFlowConflictResolver(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrPatchHandler patchHandler,
        IFileSystem fileSystem,
        ILogger<ForwardFlowConflictResolver> logger)
        : base(vmrInfo, patchHandler, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
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
        var conflictedFiles = await TryMergingBranch(vmr, targetBranch, branchToMerge, cancellationToken);

        if (!conflictedFiles.Any())
        {
            return [];
        }

        if (!await TryResolvingConflicts(
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

    private async Task<bool> TryResolvingConflicts(
        string mappingName,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        IReadOnlyCollection<UnixPath> conflictedFiles,
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

            await AbortMerge(vmr);
            return false;
        }

        return true;
    }

    private async Task<bool> TryResolvingConflict(
        string mappingName,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        UnixPath conflictedFile,
        UnixPath[] allowedConflicts,
        ForwardFlow currentFlow,
        Codeflow? recentFlow,
        CancellationToken cancellationToken)
    {
        // Known conflict in source-manifest.json
        if (string.Equals(conflictedFile, VmrInfo.DefaultRelativeSourceManifestPath, StringComparison.OrdinalIgnoreCase))
        {
            await TryResolvingSourceManifestConflict(vmr, mappingName!, cancellationToken);
            return true;
        }

        // Known conflicts are resolved to "ours" version (the PR version)
        if (allowedConflicts.Any(allowed => conflictedFile.Path.Equals(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Auto-resolving conflict in {file} using PR version", conflictedFile);
            await vmr.ResolveConflict(conflictedFile, ours: true);
            return true;
        }

        // Unknown conflict, but can be conflicting with a out-of-order recent flow
        // Check DetectRecentFlow documentation for more details
        if (recentFlow != null)
        {
            return await TryResolvingConflictUsingRecentFlow(
                mappingName,
                vmr,
                sourceRepo,
                conflictedFile,
                currentFlow,
                recentFlow,
                cancellationToken);
        }

        return false;
    }

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
}
