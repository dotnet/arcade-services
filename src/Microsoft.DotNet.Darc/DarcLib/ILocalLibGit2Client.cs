// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using LibGit2Sharp;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// Richer implementations of local git repo operations utilizing LibGit2Sharp.
/// </summary>
public interface ILocalLibGit2Client : ILocalGitClient, IGitRepo
{
    /// <summary>
    ///     Checkout the repo to the specified state.
    /// </summary>
    /// <param name="repoDir">Path to a git repository</param>
    /// <param name="refToCheckout">Tag, branch, or commit to checkout</param>
    /// <param name="force">Force clean of the repo/submodules</param>
    void Checkout(string repoDir, string refToCheckout, bool force = false);

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

    /// <summary>
    /// This function works around a couple common issues when checking out files in LibGit2Sharp.
    /// 1. First attempt a normal whole-repo checkout at the specified treeish.
    /// 2. This could fail for two reasons: long path names on Windows, or the treeish not being resolved on any platform.
    /// 3. If the exception is one of the ones we know LibGit2Sharp to throw for max path issues, we try to checkout files individually.
    ///     1. Get the root tree.
    ///     2. For each item in the tree, try to check it out.
    ///     3. If that fails and the item is also a tree, recurse on #2.
    /// 4. Otherwise, assume that the treeish specified needs to be resolved.  Resolve it, then...
    /// 5. Attempt a normal whole-repo checkout at the resolved treeish.
    /// 6. If this fails with one of the exception types linked to MAX_PATH issues, do #3 with the resolved treeish.
    /// This will still fail if the specified treeish doesn't resolve, or if checkout fails for any other reason than MAX_PATH.
    /// </summary>
    /// <param name="repo">Repo to check out the files from</param>
    /// <param name="commit">Commit, tag, or branch to checkout the files at</param>
    /// <param name="options">Checkout options - mostly whether to force</param>
    void SafeCheckout(Repository repo, string commit, CheckoutOptions options);
}
