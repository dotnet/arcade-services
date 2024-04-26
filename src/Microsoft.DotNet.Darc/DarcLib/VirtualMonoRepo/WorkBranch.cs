// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
        var result = await _repo.ExecuteGitCommand(
            [ "merge", _workBranch, "--no-commit", "--no-ff", "--no-edit", "--no-squash", "-q" ]);

        result.ThrowIfFailed($"Failed to merge work branch {_workBranch} into {OriginalBranch}");

        await _repo.CommitAsync(commitMessage, true);
    }
}
