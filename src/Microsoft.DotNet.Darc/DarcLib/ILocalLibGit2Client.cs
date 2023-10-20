// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// Richer implementations of local git repo operations utilizing LibGit2Sharp.
/// </summary>
public interface ILocalLibGit2Client : ILocalGitClient
{
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
    /// <param name="author">Identity object containing username and email. Defaults to DarcBot identity</param>
    Task CommitAsync(
        string repoPath,
        string message,
        bool allowEmpty,
        Identity? author = null,
        CancellationToken cancellationToken = default) =>
        CommitAsync(repoPath, message, allowEmpty, author == null ? null : (author.Name, author.Email), cancellationToken);

    /// <summary>
    ///     Commit or update a set of files to a repo
    /// </summary>
    /// <param name="filesToCommit">Files to comit</param>
    /// <param name="repoUri">Remote repository URI</param>
    /// <param name="branch">Branch to push to</param>
    /// <param name="commitMessage">Commit message</param>
    Task CommitFilesAsync(List<GitFile> filesToCommit, string repoPath, string branch, string commitMessage);

    /// <summary>
    ///     Pushes a branch to a remote
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
        Identity? identity = null);
}
