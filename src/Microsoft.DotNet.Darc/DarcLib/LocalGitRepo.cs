// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// This class can manage a specific local git repository.
/// </summary>
public class LocalGitRepo(NativePath repoPath, ILocalGitClient localGitClient) : ILocalGitRepo
{
    public NativePath Path { get; } = repoPath;

    private readonly ILocalGitClient _localGitClient = localGitClient;

    public LocalGitRepo(string path, ILocalGitClient localGitClient) : this(new NativePath(path), localGitClient)
    {
    }

    public void AddGitAuthHeader(IList<string> args, IDictionary<string, string> envVars, string repoUri)
        => _localGitClient.AddGitAuthHeader(args, envVars, repoUri);

    public Task<string> AddRemoteIfMissingAsync(string repoUrl, CancellationToken cancellationToken = default)
        => _localGitClient.AddRemoteIfMissingAsync(Path, repoUrl, cancellationToken);

    public Task<string> BlameLineAsync(string relativeFilePath, int line, string? blameFromCommit = null)
        => _localGitClient.BlameLineAsync(Path, relativeFilePath, line, blameFromCommit);

    public Task CheckoutAsync(string refToCheckout)
        => _localGitClient.CheckoutAsync(Path, refToCheckout);

    public Task CommitAsync(string message, bool allowEmpty, (string Name, string Email)? author = null, CancellationToken cancellationToken = default)
        => _localGitClient.CommitAsync(Path, message, allowEmpty, author, cancellationToken);

    public Task CreateBranchAsync(string branchName, bool overwriteExistingBranch = false)
        => _localGitClient.CreateBranchAsync(Path, branchName, overwriteExistingBranch);

    public Task<string?> GetFileFromGitAsync(string relativeFilePath, string revision = "HEAD", string? outputPath = null)
        => _localGitClient.GetFileFromGitAsync(Path, relativeFilePath, revision, outputPath);

    public Task<string> GetGitCommitAsync(CancellationToken cancellationToken = default)
        => _localGitClient.GetGitCommitAsync(Path, cancellationToken);

    public Task<List<GitSubmoduleInfo>> GetGitSubmodulesAsync(string commit)
        => _localGitClient.GetGitSubmodulesAsync(Path, commit);

    public Task<GitObjectType> GetObjectTypeAsync(string objectSha)
        => _localGitClient.GetObjectTypeAsync(Path, objectSha);

    public Task<string> GetRootDirAsync(CancellationToken cancellationToken = default)
        => _localGitClient.GetRootDirAsync(Path, cancellationToken);

    public Task<string> GetShaForRefAsync(string? gitRef = null)
        => _localGitClient.GetShaForRefAsync(Path, gitRef);

    public Task<string[]> GetStagedFiles()
        => _localGitClient.GetStagedFiles(Path);

    public Task ResetWorkingTree(UnixPath? relativePath = null)
        => _localGitClient.ResetWorkingTree(new NativePath(Path), relativePath);

    public Task StageAsync(IEnumerable<string> pathsToStage, CancellationToken cancellationToken = default)
        => _localGitClient.StageAsync(Path, pathsToStage, cancellationToken);

    public Task UpdateRemoteAsync(string remoteName, CancellationToken cancellationToken = default)
        => _localGitClient.UpdateRemoteAsync(Path, remoteName, cancellationToken);
}

public interface ILocalGitRepoFactory
{
    ILocalGitRepo Create(string repoPath);
}

public class LocalGitRepoFactory(ILocalGitClient localGitClient) : ILocalGitRepoFactory
{
    public ILocalGitRepo Create(string repoPath) => new LocalGitRepo(repoPath, localGitClient);
}
