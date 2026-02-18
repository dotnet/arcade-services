// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
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

        IReadOnlyCollection<UnixPath> conflictedFiles = (await targetRepo.GetStagedFilesAsync()).Count > 0
            ? await targetRepo.GetConflictedFilesAsync(cancellationToken)
            : await TryMergingBranch(targetRepo, codeflowOptions.HeadBranch, codeflowOptions.TargetBranch, cancellationToken);

        if (conflictedFiles.Count != 0)
        {
            await TryResolvingConflicts(
                codeflowOptions,
                vmr,
                productRepo,
                conflictedFiles,
                lastFlows.CrossingFlow,
                headBranchExisted,
                cancellationToken);
        }

        return await targetRepo.GetConflictedFilesAsync(cancellationToken);
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
                if (!await TryResolvingConflict(codeflowOptions, vmr, sourceRepo, filePath, crossingFlow, headBranchExisted, cancellationToken))
                {
                    _logger.LogInformation("Failed to auto-resolve a conflict in {conflictedFile}", filePath);
                    // When we fail to resolve conflicts during a rebase, we are fine with keeping it
                    success = false;
                    continue;
                }

                count++;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to resolve conflicts in {filePath}", filePath);
                success = false;
            }
        }

        if (success)
        {
            _logger.LogInformation("Successfully auto-resolved {count} expected conflicts", count);
        }
        else
        {
            _logger.LogInformation("Auto-resolved {count} expected conflicts, some remain unresolved", count);
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

        UnixPath vmrRepoPath = VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name);
        if (codeflowOptions.CurrentFlow.IsForwardFlow && !conflictedFile.Path.StartsWith(vmrRepoPath + '/'))
        {
            _logger.LogWarning("Conflict in {file} is not in the source repo, skipping auto-resolution", conflictedFile);
            return false;
        }

        // Create patch for the file represent only the most current flow
        var (fromSha, toSha) = codeflowOptions.CurrentFlow.IsForwardFlow
            ? (crossingFlow.RepoSha, codeflowOptions.CurrentFlow.RepoSha)
            : (crossingFlow.VmrSha, codeflowOptions.CurrentFlow.VmrSha);

        var patchName = _vmrInfo.TmpPath / $"{codeflowOptions.Mapping.Name}-{Guid.NewGuid()}.patch";
        List<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            patchName,
            fromSha,
            toSha,
            path: codeflowOptions.CurrentFlow.IsForwardFlow
                ? new UnixPath(conflictedFile.Path.Substring(vmrRepoPath.Length + 1))
                : conflictedFile,
            filters: null,
            relativePaths: true,
            workingDir: codeflowOptions.CurrentFlow.IsForwardFlow
                ? repo.Path
                : vmr.Path / vmrRepoPath,
            applicationPath: codeflowOptions.CurrentFlow.IsForwardFlow
                ? vmrRepoPath
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

        var targetRepo = codeflowOptions.CurrentFlow.IsForwardFlow ? vmr : repo;
        try
        {
            await targetRepo.ResolveConflict(conflictedFile, ours: true);
        }
        // file does not exist in the target repo anymore
        catch (ProcessFailedException e) when (e.Message.Contains("does not have our version"))
        {
            _logger.LogInformation("Detected conflicts in {filePath}", conflictedFile);
            return false;
        }

        if (_fileSystem.GetFileInfo(patches.First().Path).Length == 0)
        {
            // This file did not change in the last flow
            return true;
        }

        try
        {
            await _patchHandler.ApplyPatch(
                patches[0],
                targetRepo.Path,
                removePatchAfter: true,
                keepConflicts: false,
                cancellationToken: cancellationToken);
            _logger.LogDebug("Successfully auto-resolved a conflict in {filePath}", conflictedFile);

            await targetRepo.ExecuteGitCommand(["restore", conflictedFile], cancellationToken);

            return true;
        }
        catch (PatchApplicationFailedException)
        {
            // If the patch failed, we cannot resolve the conflict automatically
            // We will just leave it as is and let the user resolve it manually
            _logger.LogInformation("Detected conflicts in {filePath}", conflictedFile);

            // Revert the file into the conflicted state for manual resolution
            await targetRepo.ExecuteGitCommand(["checkout", "--conflict=merge", "--", conflictedFile], CancellationToken.None);

            return false;
        }
    }

    /// <summary>
    /// If a file was added and then removed again in the original repo, it won't exist in the PR branch,
    /// so in case of a conflict + recreation, we cannot remove it (if it does not exist).
    /// In non-rebase flow, we stick custom content in the file - a message to indicate it should be removed.
    /// In rebase, we can remove it after when we see this content. This method does just that.
    /// </summary>
    protected async Task<bool> TryDeletingFileMarkedForDeletion(ILocalGitRepo repo, UnixPath conflictedFile, CancellationToken cancellationToken)
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

    /// <summary>
    /// Tries to reverse apply the latest flow to detect partial reverts and fix them using the crossing flow.
    /// If the current flow does not apply in reverse, it means some changes were lost (a revert happened).
    /// </summary>
    protected async Task DetectAndFixPartialReverts(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo vmr,
        ILocalGitRepo productRepo,
        IReadOnlyCollection<UnixPath> conflictedFiles,
        LastFlows lastFlows,
        CancellationToken cancellationToken)
    {
        if (lastFlows.CrossingFlow == null)
        {
            // Reverts can only happen if there was a crossing flow
            return;
        }

        // Create patch representing the current flow (minus the recreated previous flows)
        var (fromSha, toSha) = codeflowOptions.CurrentFlow.IsForwardFlow
            ? (lastFlows.CrossingFlow.RepoSha, codeflowOptions.CurrentFlow.RepoSha)
            : (lastFlows.CrossingFlow.VmrSha, codeflowOptions.CurrentFlow.VmrSha);

        var targetRepo = codeflowOptions.CurrentFlow.IsForwardFlow ? vmr : productRepo;
        var vmrSourcesPath = VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping);

        // We create the patches minus the version, conflicted and cloaked files
        IReadOnlyCollection<string> excludedFiles =
        [
            .. conflictedFiles.Select(conflictedFile => VmrPatchHandler.GetExclusionRule(codeflowOptions.CurrentFlow.IsForwardFlow
                ? new UnixPath(conflictedFile.Path.Substring(vmrSourcesPath.Length + 1))
                : conflictedFile)),
            .. GetPatchExclusions(codeflowOptions.Mapping),
        ];

        // We create the patch minus the version, conflicted and cloaked files
        excludedFiles =
        [
            .. conflictedFiles.Select(conflictedFile => VmrPatchHandler.GetExclusionRule(codeflowOptions.CurrentFlow.IsForwardFlow
                ? new UnixPath(conflictedFile.Path.Substring(vmrSourcesPath.Length + 1))
                : conflictedFile)),
            .. GetPatchExclusions(codeflowOptions.Mapping),
        ];

        List<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            _vmrInfo.TmpPath / $"{codeflowOptions.Mapping.Name}-{Guid.NewGuid()}.patch",
            fromSha,
            toSha,
            path: null,
            filters: [..excludedFiles.Distinct()],
            relativePaths: true,
            workingDir: codeflowOptions.CurrentFlow.IsForwardFlow
                ? productRepo.Path
                : _vmrInfo.GetRepoSourcesPath(codeflowOptions.Mapping),
            applicationPath: codeflowOptions.CurrentFlow.IsForwardFlow
                ? vmrSourcesPath
                : null,
            ignoreLineEndings: true,
            cancellationToken);

        // We try to reverse-apply the current changes - if we fail, it means there is a missing change
        try
        {
            await _patchHandler.ApplyPatches(
                patches,
                targetRepo.Path,
                removePatchAfter: false,
                keepConflicts: false,
                reverseApply: true,
                applyToIndex: false,
                cancellationToken);
        }
        catch (PatchApplicationFailedException e)
        {
            var revertedFiles = e.Result.StandardError
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("error:") && line.EndsWith(": patch does not apply"))
                .Select(line => new UnixPath(line.Substring(7, line.Length - 29).Trim()));

            if (!revertedFiles.Any())
            {
                _logger.LogError(e, "Failed to detect reverted files from patch application failure");
                _logger.LogDebug(e.Result.ToString());
                return;
            }

            await FixRevertedFiles(
                codeflowOptions,
                vmr,
                productRepo,
                lastFlows.CrossingFlow,
                revertedFiles,
                cancellationToken);
        }
    }

    private async Task FixRevertedFiles(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo vmr,
        ILocalGitRepo productRepo,
        Codeflow crossingFlow,
        IEnumerable<UnixPath> revertedFiles,
        CancellationToken cancellationToken)
    {
        var targetRepo = codeflowOptions.CurrentFlow.IsForwardFlow ? vmr : productRepo;

        foreach (var revertedFile in revertedFiles)
        {
            _logger.LogInformation("Suspecting a revert in {file}. Trying to fix it using a crossing flow...",
                revertedFile);

            if (!await CheckIfRealRevertAsync(
                revertedFile,
                codeflowOptions,
                crossingFlow, vmr,
                productRepo,
                cancellationToken))
            {
                continue;
            }

            string contentBefore = await _fileSystem.ReadAllTextAsync(targetRepo.Path / revertedFile);

            (await targetRepo.ExecuteGitCommand(["checkout", codeflowOptions.TargetBranch, revertedFile], cancellationToken))
                .ThrowIfFailed($"Failed to check out {revertedFile} from branch {codeflowOptions.TargetBranch}");

            if (!await TryResolvingConflictWithCrossingFlow(
                codeflowOptions,
                vmr,
                productRepo,
                revertedFile,
                crossingFlow,
                cancellationToken))
            {
                _logger.LogInformation("Failed to auto-resolve a conflict in {file} while fixing a partial revert",
                    revertedFile);
                _fileSystem.WriteToFile(targetRepo.Path / revertedFile, contentBefore);
                await targetRepo.StageAsync([revertedFile], cancellationToken);
            }
        }
    }

    /// To check if a file actually got reverted, we need to strip it of the changes since the last crossing flow.
    /// Returns true if a real reverted happened, false if it was a false positive
    private async Task<bool> CheckIfRealRevertAsync(
        UnixPath filePath,
        CodeflowOptions codeflowOptions,
        Codeflow crossingFlow,
        ILocalGitRepo vmr,
        ILocalGitRepo productRepo,
        CancellationToken cancellationToken)
    {
        var vmrRepoSourcesPath = _vmrInfo.GetRepoSourcesPath(codeflowOptions.Mapping);

        ILocalGitRepo targetRepo;
        string stripPatchFromSha;
        string reverseApplyFromSha, reverseApplyToSha;
        NativePath stripPatchWorkingDir, reverseApplyWorkingDir;
        UnixPath? applicationPath;
        UnixPath relativeFilePath;

        if (codeflowOptions.CurrentFlow.IsForwardFlow)
        {
            targetRepo = vmr;
            stripPatchFromSha = crossingFlow.VmrSha;
            stripPatchWorkingDir = vmrRepoSourcesPath;
            reverseApplyFromSha = crossingFlow.RepoSha;
            reverseApplyToSha = codeflowOptions.CurrentFlow.RepoSha;
            reverseApplyWorkingDir = productRepo.Path;
            applicationPath = VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping);
            relativeFilePath = new UnixPath(filePath.Path.Substring(VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name).Length + 1));
        }
        else
        {
            targetRepo = productRepo;
            stripPatchFromSha = crossingFlow.RepoSha;
            stripPatchWorkingDir = productRepo.Path;
            reverseApplyFromSha = crossingFlow.VmrSha;
            reverseApplyToSha = codeflowOptions.CurrentFlow.VmrSha;
            reverseApplyWorkingDir = vmrRepoSourcesPath;
            applicationPath = null;
            relativeFilePath = filePath;
        }
        string contentBefore = await _fileSystem.ReadAllTextAsync(targetRepo.Path / filePath);

        // strip the changes in the target repo that might be causing a false positive
        var stripCrossingFlowChangesPatch = await _patchHandler.CreatePatches(
            _vmrInfo.TmpPath / $"{codeflowOptions.Mapping.Name}-{Guid.NewGuid()}.patch",
            stripPatchFromSha,
            Constants.HEAD,
            path: relativeFilePath,
            filters: [],
            relativePaths: true,
            workingDir: stripPatchWorkingDir,
            applicationPath: applicationPath,
            ignoreLineEndings: true,
            cancellationToken);
        await _patchHandler.ApplyPatches(
            stripCrossingFlowChangesPatch,
            targetRepo.Path,
            removePatchAfter: true,
            keepConflicts: false,
            reverseApply: true,
            applyToIndex: false,
            cancellationToken);

        // now reverse apply the current flow's changes. If it fails, it was a real revert; otherwise a false positive
        List<VmrIngestionPatch> reverseApplyPatch = await _patchHandler.CreatePatches(
            _vmrInfo.TmpPath / $"{codeflowOptions.Mapping.Name}-{Guid.NewGuid()}.patch",
            reverseApplyFromSha,
            reverseApplyToSha,
            path: relativeFilePath,
            filters: [],
            relativePaths: true,
            workingDir: reverseApplyWorkingDir,
            applicationPath: applicationPath,
            ignoreLineEndings: true,
            cancellationToken);

        try
        {
            await _patchHandler.ApplyPatches(
                reverseApplyPatch,
                targetRepo.Path,
                removePatchAfter: false,
                keepConflicts: false,
                reverseApply: true,
                applyToIndex: false,
                cancellationToken);
            return false;
        }
        catch (PatchApplicationFailedException)
        {
            return true;
        }
        finally
        {
            _fileSystem.WriteToFile(targetRepo.Path / filePath, contentBefore);
            await targetRepo.StageAsync([filePath], cancellationToken);
        }
    }

    protected virtual IEnumerable<string> GetPatchExclusions(SourceMapping mapping) =>
    [
        .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
        .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
        .. DependencyFileManager.CodeflowDependencyFiles.Select(VmrPatchHandler.GetExclusionRule),
    ];
}
