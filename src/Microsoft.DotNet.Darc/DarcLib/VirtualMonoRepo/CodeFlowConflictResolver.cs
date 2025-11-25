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

public abstract class CodeFlowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;

    protected CodeFlowConflictResolver(
        IVmrInfo vmrInfo,
        IVmrPatchHandler patchHandler,
        IFileSystem fileSystem,
        ILogger logger)
    {
        _vmrInfo = vmrInfo;
        _patchHandler = patchHandler;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    protected async Task<IReadOnlyCollection<UnixPath>> TryMergingBranch(
        ILocalGitRepo repo,
        string headBranch,
        string branchToMerge,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Trying to merge {branchToMerge} into {headBranch}...", branchToMerge, headBranch);

        await repo.CheckoutAsync(headBranch);
        var result = await repo.RunGitCommandAsync(["merge", "--no-commit", "--no-ff", branchToMerge], cancellationToken);
        if (result.Succeeded)
        {
            try
            {
                await repo.CommitAsync(
                    $"Merge {branchToMerge} into {headBranch}",
                    allowEmpty: false,
                    cancellationToken: CancellationToken.None);

                _logger.LogInformation("Successfully merged the branch {headBranch} into {headBranch} in {repoPath}",
                    branchToMerge,
                    headBranch,
                    repo.Path);
            }
            catch (Exception e) when (e.Message.Contains("nothing to commit"))
            {
                // Our branch might be fast-forward and so no commit was needed
                _logger.LogInformation("Branch {headBranch} had no updates since it was last merged into {headBranch}",
                    branchToMerge,
                    headBranch);
            }

            return [];
        }
        else
        {
            result = await repo.RunGitCommandAsync(["diff", "--name-only", "--diff-filter=U", "--relative"], cancellationToken);
            if (!result.Succeeded)
            {
                await AbortMerge(repo);
                result.ThrowIfFailed("Failed to resolve version file conflicts - failed to get a list of conflicted files");
                throw new InvalidOperationException(); // the line above will throw, including more details
            }

            return result
                .GetOutputLines()
                .Select(line => new UnixPath(line))
                .ToList();
        }
    }

    /// <summary>
    /// If a recent crossing flow is detected, we can try to figure out if the changes that happened to it in the repo
    /// apply on top of the VMR file.
    /// </summary>
    /// <returns>True when auto-resolution succeeded</returns>
    protected async Task<bool> TryResolvingConflictWithCrossingFlow(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo vmr,
        ILocalGitRepo repo,
        UnixPath conflictedFile,
        Codeflow crossingFlow,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Trying to auto-resolve a conflict in {filePath} based on a crossing flow...", conflictedFile);

        UnixPath vmrSourcesPath = VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name);
        if (codeflowOptions.CurrentFlow.IsForwardFlow && !conflictedFile.Path.StartsWith(vmrSourcesPath + '/'))
        {
            _logger.LogWarning("Conflict in {file} is not in the source repo, skipping auto-resolution", conflictedFile);
            return false;
        }

        var targetRepo = codeflowOptions.CurrentFlow.IsForwardFlow ? vmr : repo;

        await targetRepo.ResolveConflict(conflictedFile, ours: codeflowOptions.EnableRebase /* rebase vs merge direction */);

        var patchName = _vmrInfo.TmpPath / $"{codeflowOptions.Mapping.Name}-{Guid.NewGuid()}.patch";
        List<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            patchName,
            crossingFlow.SourceSha,
            codeflowOptions.CurrentFlow.SourceSha,
            codeflowOptions.CurrentFlow.IsForwardFlow
                ? new UnixPath(conflictedFile.Path.Substring(vmrSourcesPath.Length + 1))
                : conflictedFile,
            filters: null,
            relativePaths: true,
            workingDir: codeflowOptions.CurrentFlow.IsForwardFlow ? repo.Path : vmr.Path / vmrSourcesPath,
            applicationPath: codeflowOptions.CurrentFlow.IsForwardFlow ? vmrSourcesPath : null,
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
                targetRepo.Path,
                removePatchAfter: true,
                keepConflicts: false,
                cancellationToken: cancellationToken);
            _logger.LogInformation("Successfully auto-resolved a conflict in {filePath}", conflictedFile);

            if (codeflowOptions.EnableRebase)
            {
                // Stage the file for commit
                await targetRepo.StageAsync([conflictedFile], CancellationToken.None);
            }

            return true;
        }
        catch (PatchApplicationFailedException)
        {
            // If the patch failed, we cannot resolve the conflict automatically
            // We will just leave it as is and let the user resolve it manually
            _logger.LogDebug("Failed to auto-resolve conflicts in {filePath} - conflicting changes detected", conflictedFile);

            if (codeflowOptions.EnableRebase)
            {
                // Revert the file into the conflicted state for manual resolution
                await targetRepo.ExecuteGitCommand(["checkout", "--conflict=merge", "--", conflictedFile], CancellationToken.None);
            }

            return false;
        }
    }

    protected static async Task AbortMerge(ILocalGitRepo repo)
    {
        var result = await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
        result.ThrowIfFailed("Failed to abort a merge when resolving version file conflicts");
    }
}
