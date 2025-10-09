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
    Task RebaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Name of the original branch where the work branch was created from.
    /// </summary>
    string OriginalBranchName { get; }

    /// <summary>
    /// Name of the work branch.
    /// </summary>
    string WorkBranchName { get; }
}

/// <summary>
/// Helper class that creates a new git branch when initialized and can merge this branch back into the original branch.
/// </summary>
public class WorkBranch(
        ILocalGitRepo repo,
        ILogger logger,
        string originalBranch,
        string workBranch)
    : IWorkBranch
{
    private readonly ILogger _logger = logger;
    private readonly ILocalGitRepo _repo = repo;

    public string OriginalBranchName { get; } = originalBranch;

    public string WorkBranchName { get; } = workBranch;

    public async Task MergeBackAsync(string commitMessage)
    {
        _logger.LogInformation("Merging {branchName} into {mainBranch}", WorkBranchName, OriginalBranchName);

        await _repo.CheckoutAsync(OriginalBranchName);

        var mergeArgs = new[] { "merge", WorkBranchName, "--no-commit", "--no-edit", "--squash", "-q" };

        var result = await _repo.ExecuteGitCommand(mergeArgs);

        async Task CheckConflicts()
        {
            var conflictedFiles = await _repo.GetConflictedFilesAsync(CancellationToken.None);
            if (conflictedFiles.Count > 0)
            {
                await _repo.ExecuteGitCommand(["reset", "--hard"]);
                throw new WorkBranchInConflictException(WorkBranchName, OriginalBranchName, result);
            }
        }

        // If we failed, it might be because the working tree is dirty because of EOL changes
        // These are uninteresting because the index will have the correct EOLs
        if (!result.Succeeded)
        {
            // If we failed, it might be because of a merge conflict
            await CheckConflicts();

            // Let's see if that is the case
            var diffResult = await _repo.ExecuteGitCommand(["diff", "-w"]);
            if (diffResult.Succeeded)
            {
                _logger.LogWarning("Update succeeded but some EOL file changes were not properly stashes. Ignoring these...");

                // We stage those changes, they will get absorbed with upcoming merge
                result = await _repo.ExecuteGitCommand(["add", "-A"]);
                result.ThrowIfFailed($"Failed to stage whitespace-only EOL changes while merging work branch {WorkBranchName} into {OriginalBranchName}");
                result = await _repo.ExecuteGitCommand(mergeArgs);
            }
        }

        if (!result.Succeeded)
        {
            await CheckConflicts();
        }

        await _repo.CommitAsync(commitMessage, allowEmpty: true);
        await _repo.DeleteBranchAsync(WorkBranchName);
    }

    public async Task RebaseAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rebasing {branchName} onto {mainBranch}...", WorkBranchName, OriginalBranchName);

        cancellationToken.ThrowIfCancellationRequested();
        await _repo.CheckoutAsync(OriginalBranchName);

        try
        {
            var result = await _repo.ExecuteGitCommand(["merge", "--squash", WorkBranchName], cancellationToken);
            if (!result.Succeeded)
            {
                IReadOnlyCollection<UnixPath> conflictedFiles = await _repo.GetConflictedFilesAsync(CancellationToken.None);
                throw new PatchApplicationLeftConflictsException(conflictedFiles, _repo.Path);
            }
        }
        finally
        {
            await _repo.DeleteBranchAsync(WorkBranchName);
        }
    }
}

public class WorkBranchInConflictException(string workBranch, string targetBranch, ProcessExecutionResult executionResult)
    : ProcessFailedException(executionResult, $"Failed to merge back the work branch {workBranch} into {targetBranch}")
{
}
