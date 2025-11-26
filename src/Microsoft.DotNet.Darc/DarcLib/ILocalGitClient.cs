// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;

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
    ///    Retrieves a list of remotes in the repository.
    /// </summary>
    Task<List<(string Name, string Uri)>> GetRemotesAsync(string repoPath);

    /// <summary>
    ///     Retrieves SHA of the commit that last changed the given line in the given file.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="relativeFilePath">Relative path to the file inside of the repository</param>
    /// <param name="line">Line to blame</param>
    /// <param name="blameFromCommit">Blame older commits than a given one</param>
    /// <returns>SHA of the commit that last changed the given line in the given file</returns>
    Task<string> BlameLineAsync(
        string repoPath,
        string relativeFilePath,
        int line,
        string? blameFromCommit = null);

    /// <summary>
    /// Finds a given line in a file and returns the SHA of the commit that last changed it.
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <param name="isTargetLine">Predicate to tell the line in question</param>
    /// <param name="blameFromCommit">Blame older commits than a given one</param>
    Task<string> BlameLineAsync(
        string filePath,
        Func<string, bool> isTargetLine,
        string? blameFromCommit = null);

    /// <summary>
    ///     Checks if the repository has any working tree changes.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    Task<bool> HasWorkingTreeChangesAsync(string repoPath);

    /// <summary>
    ///    Checks if the repository has any staged changes.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    Task<bool> HasStagedChangesAsync(string repoPath);

    /// <summary>
    ///     Checkout the repo to the specified state.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="refToCheckout">Tag, branch, or commit to checkout</param>
    Task CheckoutAsync(string repoPath, string refToCheckout);

    /// <summary>
    ///     Checkout the repo to the specified state using the force flag.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="refToCheckout">Tag, branch, or commit to checkout</param>
    Task ForceCheckoutAsync(string repoPath, string refToCheckout);

    /// <summary>
    ///     Resets the working tree (or a given subpath) to match the index.
    /// </summary>
    /// <param name="repoPath">Path to the root of the repo</param>
    /// <param name="relativePath">Relative path inside of the repo to reset only (or none if the whole repo)</param>
    Task ResetWorkingTree(NativePath repoPath, UnixPath? relativePath = null);

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
    ///     Commit amends the staged changes by calling git commit --amend
    /// </summary>
    /// <param name="repoPath">Path of the local repository</param>
    /// <returns></returns>
    Task CommitAmendAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a local branch.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="branchName">New branch name</param>
    /// <param name="overwriteExistingBranch">Whether to overwrite an already existing branch</param>
    Task CreateBranchAsync(string repoPath, string branchName, bool overwriteExistingBranch = false);

    /// <summary>
    ///     Deletes a local branch.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="branchName">Name of the branch to delete</param>
    Task DeleteBranchAsync(string repoPath, string branchName);

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
        string? revision = "HEAD",
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
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="gitRef">Git reference to resolve or HEAD when null</param>
    Task<string> GetShaForRefAsync(string repoPath, string? gitRef = null);

    /// <summary>
    ///     Gets the type of a git object (e.g. commit, tree..).
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="objectSha">SHA of the object</param>
    Task<GitObjectType> GetObjectTypeAsync(string repoPath, string objectSha);

    /// <summary>
    ///     Returns a list of modified staged files.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <returns>List of currently modified staged files</returns>
    Task<IReadOnlyCollection<string>> GetStagedFilesAsync(string repoPath);

    /// <summary>
    ///     Returns a list of modified working tree files.
    /// </summary>
    /// <returns>List of currently modified staged files</returns>
    Task<IReadOnlyCollection<string>> GetDirtyFilesAsync(string repoPath);

    /// <summary>
    ///     Determines if a given path is a git repository.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="gitRef">Git reference to check for</param>
    /// <returns>True if the path is a git repository, false otherwise</returns>
    Task<bool> GitRefExists(string repoPath, string gitRef, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the type of a git reference (branch, tag, commit, remote branch, etc.).
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="gitRef">Git reference to get the type for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Type of the git reference, or Unknown if the reference doesn't exist</returns>
    Task<GitObjectType> GetRefType(string repoPath, string gitRef, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fetches from all remotes.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="remoteUris">List of remotes to fetch from</param>
    Task FetchAllAsync(
        string repoPath,
        IReadOnlyCollection<string> remoteUris,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Performs `git pull`
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    Task PullAsync(
        string repoPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stages files from the given path.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="pathsToStage">Paths that will be staged to index</param>
    Task StageAsync(
        string repoPath,
        IEnumerable<string> pathsToStage,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Add the authorization header to the git command line arguments and environment variables.
    /// </summary>
    /// <param name="args">Where to add the new argument into</param>
    /// <param name="envVars">Where to add the new variables into</param>
    Task AddGitAuthHeader(IList<string> args, IDictionary<string, string> envVars, string repoUri);

    /// <summary>
    ///     Gets a value of a given git configuration setting.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="setting">Name of the setting</param>
    /// <returns>Value of the setting</returns>
    Task<string> GetConfigValue(string repoPath, string setting);

    /// <summary>
    ///     Sets a value of a given git configuration setting.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="setting">Name of the setting</param>
    /// <param name="value">New value</param>
    Task SetConfigValue(string repoPath, string setting, string value);

    /// <summary>
    ///     Runs git with the given arguments and returns the result.
    /// </summary>
    Task<ProcessExecutionResult> RunGitCommandAsync(
        string repoPath,
        string[] args,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the current checked out branch.
    /// </summary>
    Task<string> GetCheckedOutBranchAsync(NativePath path);

    /// <summary>
    /// Compares 2 git commits and returns true if the first one is an ancestor of the second one.
    /// </summary>
    Task<bool> IsAncestorCommit(string repoPath, string ancestor, string descendant);

    /// <summary>
    ///     Resolves a conflict in a given file to ours/theirs.
    /// </summary>
    Task ResolveConflict(string repoPath, string file, bool ours);

    /// <summary>
    /// Finds the best common ancestor (merge base) between two git refs in the repository.
    /// </summary>
    Task<string> GetMergeBaseAsync(string repoPath, string gitRefA, string gitRefB);

    /// <summary>
    /// Gets a collection of file paths that have changed between the specified base and target commits or branches.
    /// </summary>
    /// <param name="baseCommitOrBranch">The commit SHA or branch name to use as the base for the comparison.</param>
    /// <param name="targetCommitOrBranch">The commit SHA or branch name to compare against the base.</param>
    /// <returns>A read-only collection of strings representing the relative paths of files that differ between the
    /// base and target. The collection is empty if there are no changed files.</returns>
    Task<IReadOnlyCollection<string>> GetChangedFilesAsync(
        string repoPath,
        string baseCommitOrBranch,
        string targetCommitOrBranch);

    /// <summary>
    /// Gets a collection of file paths that are currently in a conflicted state.
    /// </summary>
    /// <returns>A read-only collection of UnixPath representing the relative paths of files that are in conflict.
    /// The collection is empty if there are no conflicted files.</returns>
    Task<IReadOnlyCollection<UnixPath>> GetConflictedFilesAsync(
        string repoPath,
        CancellationToken cancellationToken = default);
}
