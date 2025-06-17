// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IWorkBranch
{
    Task MergeBackAsync(string commitMessage);
    string OriginalBranch { get; }
}

/// <summary>
/// Helper class that creates a new git branch when initialized and can merge this branch back into the original branch.
/// </summary>
public class WorkBranch(
    ILocalGitRepo repo,
    ILogger logger,
    string originalBranch,
    string workBranch) : IWorkBranch
{
    private readonly ILogger _logger = logger;
    private readonly ILocalGitRepo _repo = repo;
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
                result.ThrowIfFailed($"Failed to merge work branch {_workBranch} into {OriginalBranch}");
            }
        }

        await _repo.CommitAsync(commitMessage, true);
    }
}
