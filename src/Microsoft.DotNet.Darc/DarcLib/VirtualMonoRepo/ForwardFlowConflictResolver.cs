// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
            if (filePath == VmrInfo.DefaultRelativeSourceManifestPath)
            {
                await TryResolvingSourceManifestConflict(repo, _mappingName!);
                continue;
            }

            // Known conflict in a git-info props file - we just use our version as we expect it to be newer
            // TODO: For batched subscriptions, we need to handle all git-info files
            if (filePath == gitInfoFile)
            {
                await repo.RunGitCommandAsync(["checkout", "--ours", filePath]);
                await repo.StageAsync([filePath]);
                continue;
            }

            _logger.LogInformation("Failed to resolve conflicts in {file}", _vmrInfo.VmrPath);
            return false;
        }

        return true;
    }

    // TODO: This won't work for batched subscriptions
    private async Task TryResolvingSourceManifestConflict(ILocalGitRepo vmr, string mappingName)
    {
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
