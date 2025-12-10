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

    protected abstract Task<bool> TryResolvingConflict(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo vmr,
        ILocalGitRepo productRepo,
        UnixPath filePath,
        Codeflow? crossingFlow,
        bool headBranchExisted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tries to resolve well-known conflicts that can occur during a code flow operation.
    /// The conflicts can happen when backward a forward flow PRs get merged out of order
    /// and so called "crossing" flow occurs.
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
    ///       │                 └──►x 8.
    ///       │                     │
    ///
    /// In this diagram, the flows 1->5 and 3->6 are crossing each other.
    ///
    /// The conflict arises in step 8. and is caused by the fact that:
    ///   - When the forward flow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
    ///   - This means that the PR branch will be based on 1. (the real PR branch is the "actual 7.")
    ///   - This means that when 6. merged, VMR's source-manifest.json got updated with the SHA of the 3.
    ///   - So the source-manifest in 6. contains the SHA of 3.
    ///   - The forward flow PR branch contains the SHA of 5.
    ///   - So the source-manifest file conflicts on the SHA (3. vs 5.)
    ///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
    ///   - This is because basically we know we want to set the version files to point at 5.
    /// </summary>
    protected async Task<IReadOnlyCollection<UnixPath>> TryMergingBranchAndResolvingConflicts(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo vmr,
        ILocalGitRepo productRepo,
        LastFlows lastFlows,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var targetRepo = codeflowOptions.CurrentFlow.IsForwardFlow ? vmr : productRepo;

        IReadOnlyCollection<UnixPath> conflictedFiles = codeflowOptions.EnableRebase && (await targetRepo.GetStagedFilesAsync()).Count > 0
            ? await targetRepo.GetConflictedFilesAsync(cancellationToken)
            : await TryMergingBranch(targetRepo, codeflowOptions.HeadBranch, codeflowOptions.TargetBranch, cancellationToken);

        if (conflictedFiles.Count != 0
            && await TryResolvingConflicts(
                codeflowOptions,
                vmr,
                productRepo,
                conflictedFiles,
                lastFlows.CrossingFlow,
                headBranchExisted,
                cancellationToken)
            && !codeflowOptions.EnableRebase)
        {
            await targetRepo.CommitAsync(
                $"""
                Merge {codeflowOptions.TargetBranch} into {codeflowOptions.HeadBranch}
                Auto-resolved conflicts:
                - {string.Join(Environment.NewLine + "- ", conflictedFiles.Select(f => f.Path))}
                """,
                allowEmpty: true,
            cancellationToken: CancellationToken.None);
            conflictedFiles = [];
        }

        if (codeflowOptions.EnableRebase)
        {
            return await targetRepo.GetConflictedFilesAsync(cancellationToken);
        }
        else
        {
            return conflictedFiles;
        }
    }

    private async Task<IReadOnlyCollection<UnixPath>> TryMergingBranch(
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

    private async Task<bool> TryResolvingConflicts(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        IReadOnlyCollection<UnixPath> conflictedFiles,
        Codeflow? crossingFlow,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Auto-resolving expected conflicts...");
        int count = 0;
        bool success = true;

        foreach (var filePath in conflictedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await TryResolvingConflict(codeflowOptions, vmr, sourceRepo, filePath, crossingFlow, headBranchExisted, cancellationToken))
                {
                    count++;
                    continue;
                }
                else if (codeflowOptions.EnableRebase)
                {
                    // When we fail to resolve conflicts during a rebase, we are fine with keeping it
                    success = false;
                    continue;
                }
                else
                {
                    _logger.LogDebug("Conflict in {filePath} cannot be resolved automatically", filePath);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to resolve conflicts in {filePath}", filePath);
            }

            _logger.LogInformation("Failed to auto-resolve a conflict in {conflictedFile}", filePath);

            if (!codeflowOptions.EnableRebase)
            {
                await AbortMerge(codeflowOptions.CurrentFlow.IsForwardFlow ? vmr : sourceRepo);
            }
            return false;
        }

        if (success)
        {
            _logger.LogInformation("Successfully auto-resolved {count} expected conflicts", count);
        }

        return success;
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

        // Create patch for the file represent only the most current flow
        string fromSha, toSha;
        if (codeflowOptions.CurrentFlow.IsForwardFlow)
        {
            (fromSha, toSha) = (crossingFlow.RepoSha, codeflowOptions.CurrentFlow.RepoSha);
        }
        else
        {
            (fromSha, toSha) = (crossingFlow.VmrSha, codeflowOptions.CurrentFlow.VmrSha);
        }

        var patchName = _vmrInfo.TmpPath / $"{codeflowOptions.Mapping.Name}-{Guid.NewGuid()}.patch";
        List<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            patchName,
            fromSha,
            toSha,
            path: codeflowOptions.CurrentFlow.IsForwardFlow
                ? new UnixPath(conflictedFile.Path.Substring(vmrSourcesPath.Length + 1))
                : conflictedFile,
            filters: null,
            relativePaths: true,
            workingDir: codeflowOptions.CurrentFlow.IsForwardFlow
                ? repo.Path
                : vmr.Path / vmrSourcesPath,
            applicationPath: codeflowOptions.CurrentFlow.IsForwardFlow
                ? vmrSourcesPath
                : null,
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

        // If the file did not have any changes in the crossing flow but just conflicts with some older flow,
        // we are unable to resolve this.
        if (_fileSystem.GetFileInfo(patches.First().Path).Length == 0)
        {
            _logger.LogInformation("Detected conflicts in {filePath}", conflictedFile);
            return false;
        }

        var targetRepo = codeflowOptions.CurrentFlow.IsForwardFlow ? vmr : repo;
        await targetRepo.ResolveConflict(conflictedFile, ours: codeflowOptions.EnableRebase);

        try
        {
            await _patchHandler.ApplyPatch(
                patches[0],
                targetRepo.Path,
                removePatchAfter: true,
                keepConflicts: false,
                cancellationToken: cancellationToken);
            _logger.LogDebug("Successfully auto-resolved a conflict in {filePath}", conflictedFile);

            return true;
        }
        catch (PatchApplicationFailedException)
        {
            // If the patch failed, we cannot resolve the conflict automatically
            // We will just leave it as is and let the user resolve it manually
            _logger.LogInformation("Detected conflicts in {filePath}", conflictedFile);

            if (codeflowOptions.EnableRebase)
            {
                // Revert the file into the conflicted state for manual resolution
                await targetRepo.ExecuteGitCommand(["checkout", "--conflict=merge", "--", conflictedFile], CancellationToken.None);
            }

            return false;
        }
    }

    /// <summary>
    /// If a file was added and then removed again in the original repo, it won't exist in the head branch,
    /// so in case of a conflict + recreation, we cannot remove it (if it does not exist).
    /// In non-rebase flow, we stick custom content in the file - a message to indicate it should be removed.
    /// In rebase, we can remove it after when we see this content. This method does just that.
    /// </summary>
    protected async Task<bool> TryRevertingAddedFile(ILocalGitRepo repo, UnixPath conflictedFile, CancellationToken cancellationToken)
    {
        var filePath = repo.Path / conflictedFile;
        if (!_fileSystem.FileExists(filePath))
        {
            return false;
        }

        var content = await _fileSystem.ReadAllTextAsync(filePath);
        if (content?.Contains(VmrCodeFlower.FileToBeRemovedContent) ?? false)
        {
            _fileSystem.DeleteFile(filePath);
            await repo.StageAsync([conflictedFile], cancellationToken);
            _logger.LogInformation("Successfully auto-resolved a conflict in {filePath} by removing the file", conflictedFile);
            return true;
        }

        return false;
    }

    protected static async Task AbortMerge(ILocalGitRepo repo)
    {
        var result = await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
        result.ThrowIfFailed("Failed to abort a merge when resolving version file conflicts");
    }
}
