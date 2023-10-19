// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface ILocalGitRepo : IGitRepo
{
    /// <summary>
    ///     Add a remote to a local repo if does not already exist, and attempt to fetch commits.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <param name="repoUrl">URL of the remote to add</param>
    /// <returns>Name of the remote</returns>
    Task<string> AddRemoteIfMissingAsync(string repoDir, string repoUrl, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Checkout the repo to the specified state.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <param name="refToCheckout">Tag, branch, or commit to checkout</param>
    /// <param name="force">Force clean of the repo/submodules</param>
    void Checkout(string repoDir, string refToCheckout, bool force = false);

    /// <summary>
    ///    Checkout the repo to the specified state but do not use LibGit2Sharp.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <param name="refToCheckout">Tag, branch, or commit to checkout</param>
    Task CheckoutNativeAsync(string repoDir, string refToCheckout);

    /// <summary>
    /// Creates a local branch.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <param name="branchName">New branch name</param>
    /// <param name="overwriteExistingBranch">Whether to overwrite an already existing branch</param>
    Task CreateBranchAsync(string repoDir, string branchName, bool overwriteExistingBranch = false);

    /// <summary>
    ///     Commits files by calling git commit (not through Libgit2sharp)
    /// </summary>
    /// <param name="repoPath">Path of the local repository</param>
    /// <param name="message">Commit message</param>
    /// <param name="allowEmpty">Allow empty commits?</param>
    /// <param name="identity">Identity object containing username and email. Defaults to DarcBot identity</param>
    Task CommitAsync(
        string repoPath,
        string message,
        bool allowEmpty,
        LibGit2Sharp.Identity? identity = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stages files from the given path.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <param name="pathsToStage">Paths that will be staged to index</param>
    Task StageAsync(string repoDir, IEnumerable<string> pathsToStage, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the root directory of a git repo.
    /// </summary>
    /// <param name="path">Path inside of a git repository</param>
    /// <returns>Path where the .git folder resides</returns>
    Task<string> GetRootDirAsync(string? path = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the current git commit SHA.
    /// </summary>
    /// <param name="repoPath">Path to a git repository (cwd used when null)</param>
    Task<string> GetGitCommitAsync(string? repoPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the commit SHA representing given reference (branch, shortened SHA, tag, ...).
    /// </summary>
    /// <param name="repoPath">Path to a git repository (cwd used when null)</param>
    /// <param name="gitRef">Git reference to resolve or HEAD when null</param>
    Task<string> GetShaForRefAsync(string repoPath, string? gitRef);

    /// <summary>
    ///     Returns a list of git submodules registered in a given repository.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <param name="commit">Which commit the info is retrieved for</param>
    Task<List<GitSubmoduleInfo>> GetGitSubmodulesAsync(string repoDir, string commit);

    /// <summary>
    ///     Returns a list of modified staged files.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <returns>List of currently modified staged files</returns>
    IEnumerable<string> GetStagedFiles(string repoDir);

    /// <summary>
    /// Retrieves a file's content from git index (works with bare repositories).
    /// </summary>
    /// <param name="repoPath">Absolute or relative path to the repo</param>
    /// <param name="relativeFilePath">Relative path to the file inside of the repo</param>
    /// <param name="revision">Revision to get the file from</param>
    /// <param name="outputPath">Optional path to write the contents to</param>
    /// <returns>File contents</returns>
    Task<string?> GetFileFromGitAsync(string repoPath, string relativeFilePath, string revision = "HEAD", string? outputPath = null);

    /// <summary>
    /// Pushes a branch to a remote
    /// </summary>
    /// <param name="repoPath">Path of the local repository</param>
    /// <param name="branchName">Name of branch to push</param>
    /// <param name="remoteUrl">URL to push to</param>
    /// <param name="token">Token for authenticating for pushing</param>
    /// <param name="identity">Identity object containing username and email. Defaults to DarcBot identity</param>
    Task Push(
        string repoPath,
        string branchName,
        string remoteUrl,
        LibGit2Sharp.Identity? identity = null);

    /// <summary>
    /// Fetches from a given remote URI.
    /// </summary>
    /// <param name="repoPath">Path of the local repository</param>
    /// <param name="remoteUri">Remote git repository</param>
    /// <param name="token">Token to use (if any)</param>
    Task<string> FetchAsync(
        string repoPath,
        string remoteUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves SHA of the commit that last changed the given line in the given file.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="relativeFilePath">Relative path to the file inside of the repository</param>
    /// <param name="line">Line to blame</param>
    /// <returns>SHA of the commit that last changed the given line in the given file</returns>
    Task<string> BlameLineAsync(
        string repoPath,
        string relativeFilePath,
        int line);
}
