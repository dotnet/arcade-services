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
    /// <param name="skipFetch">Skip fetching remote changes</param>
    /// <returns>Name of the remote</returns>
    string AddRemoteIfMissing(string repoDir, string repoUrl, bool skipFetch = false);

    /// <summary>
    ///     Checkout the repo to the specified state.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <param name="refToCheckout">Tag, branch, or commit to checkout</param>
    /// <param name="force">Force clean of the repo/submodules</param>
    void Checkout(string repoDir, string refToCheckout, bool force = false);

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
    ///     Get the current git commit sha.
    /// </summary>
    /// <param name="repoPath">Path to a git repository (cwd used when null)</param>
    Task<string> GetGitCommitAsync(string? repoPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns a list of git submodules registered in a given repository.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <param name="commit">Which commit the info is retrieved for</param>
    List<GitSubmoduleInfo> GetGitSubmodules(string repoDir, string commit);

    /// <summary>
    ///     Returns a list of modified staged files.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <returns>List of currently modified staged files</returns>
    IEnumerable<string> GetStagedFiles(string repoDir);

    /// <summary>
    /// Pushes a branch to a remote
    /// </summary>
    /// <param name="repoPath">Path of the local repository</param>
    /// <param name="branchName">Name of branch to push</param>
    /// <param name="remoteUrl">URL to push to</param>
    /// <param name="token">Token for authenticating for pushing</param>
    /// <param name="identity">Identity object containing username and email. Defaults to DarcBot identity</param>
    void Push(
        string repoPath,
        string branchName,
        string remoteUrl,
        string? token,
        LibGit2Sharp.Identity? identity = null);
}
