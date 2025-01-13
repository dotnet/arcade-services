// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IForwardFlowConflictResolver
{
    Task<bool> TryMergingBranch(
        ILocalGitRepo vmr,
        string mappingName,
        string baseBranch,
        string targetBranch);
}

/// <summary>
/// This class is responsible for resolving well-known conflicts that can occur during a forward flow operation.
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
public class ForwardFlowConflictResolver : CodeFlowConflictResolver, IForwardFlowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ForwardFlowConflictResolver> _logger;

    public ForwardFlowConflictResolver(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IFileSystem fileSystem,
            ILogger<ForwardFlowConflictResolver> logger)
        : base(vmrInfo, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    private string? _mappingName;

    public async Task<bool> TryMergingBranch(
        ILocalGitRepo vmr,
        string mappingName,
        string targetBranch,
        string branchToMerge)
    {
        _mappingName = mappingName;
        return await TryMergingBranch(vmr, targetBranch, branchToMerge);
    }

    protected override async Task<bool> TryResolvingConflicts(ILocalGitRepo repo, IEnumerable<UnixPath> conflictedFiles)
    {
        var gitInfoFile = VmrInfo.GitInfoSourcesDir + "/" + _mappingName + ".props";
        foreach (var filePath in conflictedFiles)
        {
            // Known conflict in source-manifest.json
            if (string.Equals(filePath, VmrInfo.DefaultRelativeSourceManifestPath, StringComparison.OrdinalIgnoreCase))
            {
                await TryResolvingSourceManifestConflict(repo, _mappingName!);
                continue;
            }

            // Known conflict in a git-info props file - we just use our version as we expect it to be newer
            // TODO https://github.com/dotnet/arcade-services/issues/3378: For batched subscriptions, we need to handle all git-info files
            if (string.Equals(filePath, gitInfoFile, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Auto-resolving conflict in {file}", gitInfoFile);
                await repo.RunGitCommandAsync(["checkout", "--ours", filePath]);
                await repo.StageAsync([filePath]);
                continue;
            }

            _logger.LogInformation("Unable to resolve conflicts in {file}", _vmrInfo.VmrPath);
            return false;
        }

        return true;
    }

    // TODO https://github.com/dotnet/arcade-services/issues/3378: This won't work for batched subscriptions
    private async Task TryResolvingSourceManifestConflict(ILocalGitRepo vmr, string mappingName)
    {
        _logger.LogInformation("Auto-resolving conflict in {file}", VmrInfo.DefaultRelativeSourceManifestPath);

        // We load the source manifest from the target branch and replace the current mapping (and its submodules) with our branches' information
        var result = await vmr.RunGitCommandAsync(["show", "MERGE_HEAD:" + VmrInfo.DefaultRelativeSourceManifestPath]);

        var theirSourceManifest = SourceManifest.FromJson(result.StandardOutput);
        var ourSourceManifest = _sourceManifest;
        var updatedMapping = ourSourceManifest.Repositories.First(r => r.Path == mappingName);

        theirSourceManifest.UpdateVersion(mappingName, updatedMapping.RemoteUri, updatedMapping.CommitSha, updatedMapping.PackageVersion, updatedMapping.BarId);

        foreach (var submodule in theirSourceManifest.Submodules.Where(s => s.Path.StartsWith(mappingName + "/")))
        {
            theirSourceManifest.RemoveSubmodule(submodule);
        }

        foreach (var submodule in _sourceManifest.Submodules.Where(s => s.Path.StartsWith(mappingName + "/")))
        {
            theirSourceManifest.UpdateSubmodule(submodule);
        }

        _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, theirSourceManifest.ToJson());
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);
        await vmr.StageAsync([_vmrInfo.SourceManifestPath]);
    }
}
