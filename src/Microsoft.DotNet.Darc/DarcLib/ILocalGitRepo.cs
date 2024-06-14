// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface ILocalGitRepo
{
    /// <summary>
    ///    Path to the git repo.
    /// </summary>
    NativePath Path { get; }

    /// <summary>
    /// Executes git over the repo directory with specified arguments.
    /// </summary>
    Task<ProcessExecutionResult> ExecuteGitCommand(string[] args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes git over the repo directory with specified arguments.
    /// </summary>
    Task<ProcessExecutionResult> ExecuteGitCommand(params string[] args);

    /// <summary>
    ///     Add a remote to a local repo if does not already exist.
    /// </summary>
    /// <param name="repoUrl">URL of the remote to add</param>
    /// <returns>Name of the remote</returns>
    Task<string> AddRemoteIfMissingAsync(
        string repoUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves SHA of the commit that last changed the given line in the given file.
    /// </summary>
    /// <param name="relativeFilePath">Relative path to the file inside of the repository</param>
    /// <param name="line">Line to blame</param>
    /// <param name="blameFromCommit">Blame older commits than a given one</param>
    /// <returns>SHA of the commit that last changed the given line in the given file</returns>
    Task<string> BlameLineAsync(
        string relativeFilePath,
        int line,
        string? blameFromCommit = null);

    /// <summary>
    ///     Checkout the repo to the specified state.
    /// </summary>
    /// <param name="refToCheckout">Tag, branch, or commit to checkout</param>
    Task CheckoutAsync(string refToCheckout);

    /// <summary>
    /// Resets the working tree (or a given subpath) to match the index.
    /// </summary>
    /// <param name="relativePath">Relative path inside of the repo to reset only (or none if the whole repo)</param>
    Task ResetWorkingTree(UnixPath? relativePath = null);

    /// <summary>
    ///     Commits files by calling git commit (not through Libgit2sharp)
    /// </summary>
    /// <param name="message">Commit message</param>
    /// <param name="allowEmpty">Allow empty commits?</param>
    /// <param name="author">User name and email; defaults to DarcBot</param>
    Task CommitAsync(
        string message,
        bool allowEmpty,
        (string Name, string Email)? author = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a local branch.
    /// </summary>
    /// <param name="branchName">New branch name</param>
    /// <param name="overwriteExistingBranch">Whether to overwrite an already existing branch</param>
    Task CreateBranchAsync(string branchName, bool overwriteExistingBranch = false);

    /// <summary>
    ///     Fetches from a given remote.
    /// </summary>
    /// <param name="remoteName">Name of an already existing remote</param>
    Task UpdateRemoteAsync(string remoteName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves a file's content from git index (works with bare repositories).
    /// </summary>
    /// <param name="relativeFilePath">Relative path to the file inside of the repo</param>
    /// <param name="revision">Revision to get the file from</param>
    /// <param name="outputPath">Optional path to write the contents to</param>
    /// <returns>File contents</returns>
    Task<string?> GetFileFromGitAsync(
        string relativeFilePath,
        string revision = "HEAD",
        string? outputPath = null);

    /// <summary>
    ///     Gets the current git commit SHA.
    /// </summary>
    Task<string> GetGitCommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns a list of git submodules registered in a given repository.
    /// </summary>
    /// <param name="commit">Which commit the info is retrieved for</param>
    Task<List<GitSubmoduleInfo>> GetGitSubmodulesAsync(string commit);

    /// <summary>
    ///     Gets the root directory of a git repo.
    /// </summary>
    /// <returns>Path where the .git folder resides</returns>
    Task<string> GetRootDirAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the commit SHA representing given reference (branch, shortened SHA, tag, ...).
    /// </summary>
    /// <param name="gitRef">Git reference to resolve or HEAD when null</param>
    Task<string> GetShaForRefAsync(string? gitRef = null);

    /// <summary>
    ///     Gets the type of a git object (e.g. commit, tree..).
    /// </summary>
    /// <param name="objectSha">SHA of the object</param>
    Task<GitObjectType> GetObjectTypeAsync(string objectSha);

    /// <summary>
    /// Gets a value of a given git configuration setting.
    /// </summary>
    /// <param name="setting">Name of the setting</param>
    /// <returns>Value of the setting</returns>
    Task<string> GetConfigValue(string setting);

    /// <summary>
    /// Sets a value of a given git configuration setting.
    /// </summary>
    /// <param name="setting">Name of the setting</param>
    /// <param name="value">New value</param>
    Task SetConfigValue(string setting, string value);

    /// <summary>
    /// Fetches from all remotes.
    /// </summary>
    /// <param name="remoteUris">List of remotes to fetch from</param>
    Task FetchAllAsync(IReadOnlyCollection<string> remoteUris, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns a list of modified staged files.
    /// </summary>
    /// <returns>List of currently modified staged files</returns>
    Task<string[]> GetStagedFiles();

    /// <summary>
    ///     Stages files from the given path.
    /// </summary>
    /// <param name="pathsToStage">Paths that will be staged to index</param>
    Task StageAsync(IEnumerable<string> pathsToStage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add the authorization header to the git command line arguments and environment variables.
    /// </summary>
    /// <param name="args">Where to add the new argument into</param>
    /// <param name="envVars">Where to add the new variables into</param>
    public void AddGitAuthHeader(IList<string> args, IDictionary<string, string> envVars, string repoUri);
}
