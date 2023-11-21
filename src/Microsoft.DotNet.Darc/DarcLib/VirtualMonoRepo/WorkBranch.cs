// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
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
public class WorkBranch : IWorkBranch
{
    private readonly ILocalGitClient _localGitClient;
    private readonly IProcessManager _processManager;
    private readonly ILogger _logger;
    private readonly string _repoPath;
    private readonly string _originalBranch;
    private readonly string _workBranch;

    public string OriginalBranch => _originalBranch;

    public WorkBranch(
        ILocalGitClient localGitClient,
        IProcessManager processManager,
        ILogger logger,
        string repoPath,
        string originalBranch,
        string workBranch)
    {
        _localGitClient = localGitClient;
        _processManager = processManager;
        _logger = logger;
        _repoPath = repoPath;
        _originalBranch = originalBranch;
        _workBranch = workBranch;
    }

    public async Task MergeBackAsync(string commitMessage)
    {
        _logger.LogInformation("Merging {branchName} into {mainBranch}", _workBranch, _originalBranch);

        await _localGitClient.CheckoutAsync(_repoPath, _originalBranch);
        var result = await _processManager.ExecuteGit(
            _repoPath,
            "merge",
            _workBranch,
            "--no-commit",
            "--no-ff",
            "--no-edit",
            "--no-squash",
            "-q");

        result.ThrowIfFailed("Failed to merge work branch back into the original branch");

        await _localGitClient.CommitAsync(_repoPath, commitMessage, true);
    }
}
