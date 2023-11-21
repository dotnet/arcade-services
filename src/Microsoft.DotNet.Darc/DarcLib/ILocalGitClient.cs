// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface ILocalGitClient
{
    /// <summary>
    ///     Add a remote to a local repo if does not already exist.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="repoUrl">URL of the remote to add</param>
    /// <returns>Name of the remote</returns>
    Task<string> AddRemoteIfMissingAsync(
        string repoPath,
        string repoUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves SHA of the commit that last changed the given line in the given file.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="relativeFilePath">Relative path to the file inside of the repository</param>
    /// <param name="line">Line to blame</param>
    /// <returns>SHA of the commit that last changed the given line in the given file</returns>
    Task<string> BlameLineAsync(
        string repoPath,
        string relativeFilePath,
        int line);

    /// <summary>
    ///     Checkout the repo to the specified state.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="refToCheckout">Tag, branch, or commit to checkout</param>
    Task CheckoutAsync(string repoPath, string refToCheckout);

    /// <summary>
    ///     Commits files by calling git commit (not through Libgit2sharp)
    /// </summary>
    /// <param name="repoPath">Path of the local repository</param>
    /// <param name="message">Commit message</param>
    /// <param name="allowEmpty">Allow empty commits?</param>
    /// <param name="author">User name and email; defaults to DarcBot</param>
    Task CommitAsync(
        string repoPath,
        string message,
        bool allowEmpty,
        (string Name, string Email)? author = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a local branch.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="branchName">New branch name</param>
    /// <param name="overwriteExistingBranch">Whether to overwrite an already existing branch</param>
    Task CreateBranchAsync(string repoPath, string branchName, bool overwriteExistingBranch = false);

    /// <summary>
    ///     Fetches from a given remote.
    /// </summary>
    /// <param name="repoPath">Path of the local repository</param>
    /// <param name="remoteName">Name of an already existing remote</param>
    Task UpdateRemoteAsync(string repoPath, string remoteName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves a file's content from git index (works with bare repositories).
    /// </summary>
    /// <param name="repoPath">Absolute or relative path to the repo</param>
    /// <param name="relativeFilePath">Relative path to the file inside of the repo</param>
    /// <param name="revision">Revision to get the file from</param>
    /// <param name="outputPath">Optional path to write the contents to</param>
    /// <returns>File contents</returns>
    Task<string?> GetFileFromGitAsync(
        string repoPath,
        string relativeFilePath,
        string revision = "HEAD",
        string? outputPath = null);

    /// <summary>
    ///     Gets the current git commit SHA.
    /// </summary>
    /// <param name="repoPath">Path to a git repository (cwd used when null)</param>
    Task<string> GetGitCommitAsync(string? repoPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns a list of git submodules registered in a given repository.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="commit">Which commit the info is retrieved for</param>
    Task<List<GitSubmoduleInfo>> GetGitSubmodulesAsync(string repoPath, string commit);

    /// <summary>
    ///     Gets the root directory of a git repo.
    /// </summary>
    /// <param name="repoPath">Path inside of a git repository</param>
    /// <returns>Path where the .git folder resides</returns>
    Task<string> GetRootDirAsync(string? repoPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the commit SHA representing given reference (branch, shortened SHA, tag, ...).
    /// </summary>
    /// <param name="repoPath">Path to a git repository (cwd used when null)</param>
    /// <param name="gitRef">Git reference to resolve or HEAD when null</param>
    Task<string> GetShaForRefAsync(string repoPath, string gitRef);

    /// <summary>
    ///     Returns a list of modified staged files.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <returns>List of currently modified staged files</returns>
    Task<string[]> GetStagedFiles(string repoPath);

    /// <summary>
    ///     Stages files from the given path.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="pathsToStage">Paths that will be staged to index</param>
    Task StageAsync(
        string repoPath,
        IEnumerable<string> pathsToStage,
        CancellationToken cancellationToken = default);
}
