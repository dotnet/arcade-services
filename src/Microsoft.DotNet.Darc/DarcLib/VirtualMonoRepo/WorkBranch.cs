// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IWorkBranch
{
    /// <summary>
    /// Merges the work branch back into the original branch.
    /// </summary>
    Task MergeBackAsync(string commitMessage);

    /// <summary>
    /// Performs a custom "squash rebase" where it takes a diff from the work branch and applies it on top of the original branch.
    /// </summary>
    /// <param name="keepConflicts">Either leave conflict markers in place or fail to apply the changes completely</param>
    Task RebaseAsync(bool keepConflicts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Name of the original branch where the work branch was created from.
    /// </summary>
    string OriginalBranch { get; }
}

/// <summary>
/// Helper class that creates a new git branch when initialized and can merge this branch back into the original branch.
/// </summary>
public class WorkBranch(
        IVmrInfo vmrInfo,
        ILocalGitRepo repo,
        IVmrPatchHandler patchHandler,
        ILogger logger,
        string originalBranch,
        string workBranch)
    : IWorkBranch
{
    private readonly ILogger _logger = logger;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly ILocalGitRepo _repo = repo;
    private readonly IVmrPatchHandler _patchHandler = patchHandler;
    private readonly string _workBranch = workBranch;

    public string OriginalBranch { get; } = originalBranch;

    public async Task MergeBackAsync(string commitMessage)
    {
        _logger.LogInformation("Merging {branchName} into {mainBranch}", _workBranch, OriginalBranch);

        await _repo.CheckoutAsync(OriginalBranch);

        var mergeArgs = new[] { "merge", _workBranch, "--no-commit", "--no-edit", "--squash", "-q" };

        var result = await _repo.ExecuteGitCommand(mergeArgs);

        // If we failed, it might be because the working tree is dirty because of EOL changes
        // These are uninteresting because the index will have the correct EOLs
        if (!result.Succeeded)
        {
            // Let's see if that is the case
            var diffResult = await _repo.ExecuteGitCommand(["diff", "-w"]);
            if (diffResult.Succeeded)
            {
                _logger.LogWarning("Update succeeded but some EOL file changes were not properly stashes. Ignoring these...");

                // We stage those changes, they will get absorbed with upcoming merge
                result = await _repo.ExecuteGitCommand(["add", "-A"]);
                result.ThrowIfFailed($"Failed to stage whitespace-only EOL changes while merging work branch {_workBranch} into {OriginalBranch}");
                result = await _repo.ExecuteGitCommand(mergeArgs);
            }
        }

        if (!result.Succeeded && result.StandardError.Contains("CONFLICT (content): Merge conflict"))
        {
            // If we failed, it might be because of a merge conflict
            throw new WorkBranchInConflictException(_workBranch, OriginalBranch, result);
        }

        await _repo.CommitAsync(commitMessage, allowEmpty: true);
        await _repo.DeleteBranchAsync(_workBranch);
    }

    public async Task RebaseAsync(bool keepConflicts, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rebasing {branchName} onto {mainBranch}...", _workBranch, OriginalBranch);
        await _repo.CheckoutAsync(OriginalBranch);

        // TODO: Replace with _repo.GetMergeBaseCommitAsync() when available
        ProcessExecutionResult result = await _repo.ExecuteGitCommand(
            ["merge-base", _workBranch, OriginalBranch],
            cancellationToken);
        result.ThrowIfFailed($"Failed to find a common ancestor for {_workBranch} and {OriginalBranch}");
        string mergeBase = result.StandardOutput.Trim();

        List<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            _vmrInfo.TmpPath / $"{_workBranch.Replace('\\', '-').Replace('/', '-')}.patch",
            mergeBase,
            _workBranch,
            path: null,
            filters: [],
            relativePaths: false,
            workingDir: _repo.Path,
            applicationPath: null,
            cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        await _patchHandler.ApplyPatches(
            patches,
            _repo.Path,
            removePatchAfter: true,
            keepConflicts,
            cancellationToken: cancellationToken);

        await _repo.DeleteBranchAsync(_workBranch);
    }
}

public class WorkBranchInConflictException(string workBranch, string targetBranch, ProcessExecutionResult executionResult)
    : ProcessFailedException(executionResult, $"Failed to merge back the work branch {workBranch} into {targetBranch}")
{
}
