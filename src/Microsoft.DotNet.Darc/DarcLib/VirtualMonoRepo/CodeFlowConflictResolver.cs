// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICodeFlowConflictResolver
{
    Task<bool> TryMergingTargetBranch(string mappingName, string baseBranch, string targetBranch);
}

public class CodeFlowConflictResolver : ICodeFlowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly ISourceManifest _sourceManifest;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<CodeFlowConflictResolver> _logger;

    public CodeFlowConflictResolver(
        IVmrInfo vmrInfo,
        ILocalGitRepoFactory localGitRepoFactory,
        ISourceManifest sourceManifest,
        IFileSystem fileSystem,
        ILogger<CodeFlowConflictResolver> logger)
    {
        _vmrInfo = vmrInfo;
        _localGitRepoFactory = localGitRepoFactory;
        _sourceManifest = sourceManifest;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<bool> TryMergingTargetBranch(string mappingName, string baseBranch, string targetBranch)
    {
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        await vmr.CheckoutAsync(baseBranch);
        var result = await vmr.RunGitCommandAsync(["merge", "--no-commit", "--no-ff", targetBranch]);
        if (result.Succeeded)
        {
            _logger.LogInformation("Successfully merged the branch {targetBranch} into {headBranch} in {repoPath}",
                targetBranch,
                baseBranch,
                _vmrInfo.VmrPath);
            await vmr.CommitAsync($"Merging {targetBranch} into {baseBranch}", allowEmpty: true);
            return true;
        }

        result = await vmr.RunGitCommandAsync(["diff", "--name-only", "--diff-filter=U", "--relative"]);
        if (!result.Succeeded)
        {
            _logger.LogInformation("Failed to merge the branch {targetBranch} into {headBranch} in {repoPath}",
                targetBranch,
                baseBranch,
                _vmrInfo.VmrPath);
            result = await vmr.RunGitCommandAsync(["merge", "--abort"]);
            return false;
        }

        var conflictedFiles = result.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new UnixPath(line.Trim()));

        var gitInfoFile = VmrInfo.GitInfoSourcesDir + "/" + mappingName + ".props";

        foreach (var filePath in conflictedFiles)
        {
            // Known conflict in source-manifest.json
            if (filePath == VmrInfo.DefaultRelativeSourceManifestPath)
            {
                await TryResolvingSourceManifestConflict(vmr, mappingName);
                continue;
            }

            // Known conflict in a git-info props file - we just use our version as we expect it to be newer
            // TODO: For batched subscriptions, we need to handle all git-info files
            if (filePath == gitInfoFile)
            {
                await vmr.RunGitCommandAsync(["checkout", "--ours", filePath]);
                await vmr.StageAsync([filePath]);
                continue;
            }

            _logger.LogInformation("Failed to resolve conflicts in {file} between branches {targetBranch} and {headBranch} in {repoPath}",
                filePath,
                targetBranch,
                baseBranch,
                _vmrInfo.VmrPath);
            result = await vmr.RunGitCommandAsync(["merge", "--abort"]);
            return false;
        }

        _logger.LogInformation("Successfully resolved version file conflicts between branches {targetBranch} and {headBranch} in {repoPath}",
            targetBranch,
            baseBranch,
            _vmrInfo.VmrPath);
        await vmr.CommitAsync($"Resolving conflicts between {targetBranch} and {baseBranch}", allowEmpty: false);
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
