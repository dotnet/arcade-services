﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// This class can manage a specific local git repository.
/// </summary>
public class LocalGitRepo(NativePath repoPath, ILocalGitClient localGitClient, IProcessManager processManager)
    : ILocalGitRepo
{
    public NativePath Path { get; } = repoPath;

    private readonly ILocalGitClient _localGitClient = localGitClient;
    private readonly IProcessManager _processManager = processManager;

    public Task<ProcessExecutionResult> ExecuteGitCommand(params string[] args)
        => ExecuteGitCommand(args, default);

    public async Task<ProcessExecutionResult> ExecuteGitCommand(string[] args, CancellationToken cancellationToken = default)
        => await _processManager.ExecuteGit(Path, args, cancellationToken: cancellationToken);

    public void AddGitAuthHeader(IList<string> args, IDictionary<string, string> envVars, string repoUri)
        => _localGitClient.AddGitAuthHeader(args, envVars, repoUri);

    public async Task<string> AddRemoteIfMissingAsync(string repoUrl, CancellationToken cancellationToken = default)
        => await _localGitClient.AddRemoteIfMissingAsync(Path, repoUrl, cancellationToken);

    public async Task<string> BlameLineAsync(string relativeFilePath, int line, string? blameFromCommit = null)
        => await _localGitClient.BlameLineAsync(Path, relativeFilePath, line, blameFromCommit);

    public async Task<string> BlameLineAsync(string filePath, Func<string, bool> isTargetLine, string? blameFromCommit = null)
        => await _localGitClient.BlameLineAsync(filePath, isTargetLine, blameFromCommit);

    public async Task<bool> HasWorkingTreeChangesAsync()
        => await _localGitClient.HasWorkingTreeChangesAsync(Path);

    public async Task<bool> HasStagedChangesAsync()
        => await _localGitClient.HasStagedChangesAsync(Path);

    public async Task CheckoutAsync(string refToCheckout)
        => await _localGitClient.CheckoutAsync(Path, refToCheckout);

    public async Task CommitAsync(string message, bool allowEmpty, (string Name, string Email)? author = null, CancellationToken cancellationToken = default)
        => await _localGitClient.CommitAsync(Path, message, allowEmpty, author, cancellationToken);

    public async Task CommitAmendAsync(CancellationToken cancellationToken = default)
        => await _localGitClient.CommitAmendAsync(Path, cancellationToken);

    public async Task CreateBranchAsync(string branchName, bool overwriteExistingBranch = false)
        => await _localGitClient.CreateBranchAsync(Path, branchName, overwriteExistingBranch);

    public async Task DeleteBranchAsync(string branchName)
        => await _localGitClient.DeleteBranchAsync(Path, branchName);

    public async Task<string?> GetFileFromGitAsync(string relativeFilePath, string revision = "HEAD", string? outputPath = null)
        => await _localGitClient.GetFileFromGitAsync(Path, relativeFilePath, revision, outputPath);

    public async Task<string> GetGitCommitAsync(CancellationToken cancellationToken = default)
        => await _localGitClient.GetGitCommitAsync(Path, cancellationToken);

    public async Task<List<GitSubmoduleInfo>> GetGitSubmodulesAsync(string commit)
        => await _localGitClient.GetGitSubmodulesAsync(Path, commit);

    public async Task<GitObjectType> GetObjectTypeAsync(string objectSha)
        => await _localGitClient.GetObjectTypeAsync(Path, objectSha);

    public async Task<string> GetRootDirAsync(CancellationToken cancellationToken = default)
        => await _localGitClient.GetRootDirAsync(Path, cancellationToken);

    public async Task<string> GetShaForRefAsync(string? gitRef = null)
        => await _localGitClient.GetShaForRefAsync(Path, gitRef);

    public async Task<string> GetCheckedOutBranchAsync()
        => await _localGitClient.GetCheckedOutBranchAsync(Path);

    public async Task FetchAllAsync(IReadOnlyCollection<string> remoteUris, CancellationToken cancellationToken = default)
        => await _localGitClient.FetchAllAsync(Path, remoteUris, cancellationToken);

    public async Task PullAsync(CancellationToken cancellationToken = default)
        => await _localGitClient.PullAsync(Path, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetStagedFiles()
        => await _localGitClient.GetStagedFiles(Path);

    public async Task<string> GetConfigValue(string setting)
        => await _localGitClient.GetConfigValue(Path, setting);

    public async Task SetConfigValue(string setting, string value)
        => await _localGitClient.SetConfigValue(Path, setting, value);

    public async Task ResetWorkingTree(UnixPath? relativePath = null)
        => await _localGitClient.ResetWorkingTree(new NativePath(Path), relativePath);

    public async Task ResolveConflict(string filePath, bool ours)
        => await _localGitClient.ResolveConflict(Path, filePath, ours);

    public async Task<ProcessExecutionResult> RunGitCommandAsync(string[] args, CancellationToken cancellationToken = default)
        => await _localGitClient.RunGitCommandAsync(Path, args, cancellationToken);

    public async Task StageAsync(IEnumerable<string> pathsToStage, CancellationToken cancellationToken = default)
        => await _localGitClient.StageAsync(Path, pathsToStage, cancellationToken);

    public async Task UpdateRemoteAsync(string remoteName, CancellationToken cancellationToken = default)
        => await _localGitClient.UpdateRemoteAsync(Path, remoteName, cancellationToken);

    public async Task<bool> IsAncestorCommit(string ancestor, string descendant)
        => await _localGitClient.IsAncestorCommit(Path, ancestor, descendant);

    public override string ToString() => Path;
}

public interface ILocalGitRepoFactory
{
    ILocalGitRepo Create(NativePath repoPath);
}

public class LocalGitRepoFactory(ILocalGitClient localGitClient, IProcessManager processManager) : ILocalGitRepoFactory
{
    public ILocalGitRepo Create(NativePath repoPath) => new LocalGitRepo(repoPath, localGitClient, processManager);
}
