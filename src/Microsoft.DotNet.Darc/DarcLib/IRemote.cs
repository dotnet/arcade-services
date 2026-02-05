// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

namespace Microsoft.DotNet.DarcLib;

public interface IRemote
{
    #region Pull Request Operations

    /// <summary>
    ///  Merges a pull request created by a dependency update
    /// </summary>
    /// <param name="pullRequestUrl">Uri of pull request to merge</param>
    /// <param name="parameters">Merge options.</param>
    Task MergeDependencyPullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters);

    /// <summary>
    ///     Create new check(s), update them with a new status,
    ///     or remove each merge policy check that isn't in evaluations
    /// </summary>
    /// <param name="pullRequestUrl">Url of pull request.</param>
    /// <param name="evaluations">List of merge policies.</param>
    Task CreateOrUpdatePullRequestMergeStatusInfoAsync(string pullRequestUrl, IReadOnlyCollection<MergePolicyEvaluationResult> evaluations);

    /// <summary>
    ///     Get the checks that are being run on a pull request.
    /// </summary>
    /// <param name="pullRequestUrl">Url of pull request.</param>
    Task<IEnumerable<Check>> GetPullRequestChecksAsync(string pullRequestUrl);

    /// <summary>
    ///     Get the reviews for the specified pull request.
    /// </summary>
    /// <param name="pullRequestUrl">Url of pull request.</param>
    /// <returns>List of reviews</returns>
    Task<IEnumerable<Review>> GetPullRequestReviewsAsync(string pullRequestUrl);

    /// <summary>
    ///     Get the comments for the specified pull request.
    /// </summary>
    /// <param name="pullRequestUrl">Url of pull request.</param>
    /// <returns>List of comments</returns>
    Task<List<string>> GetPullRequestCommentsAsync(string pullRequestUrl);

    /// <summary>
    ///     Retrieve information about a pull request.
    /// </summary>
    /// <param name="pullRequestUri">URI of pull request.</param>
    /// <returns>Pull request information.</returns>
    Task<PullRequest> GetPullRequestAsync(string pullRequestUri);

    /// <summary>
    ///     Create a new pull request.
    /// </summary>
    /// <param name="repoUri">Repository uri.</param>
    /// <param name="pullRequest">Information about pull request to create.</param>
    /// <returns>Pull request information.</returns>
    Task<PullRequest> CreatePullRequestAsync(string repoUri, PullRequest pullRequest);

    /// <summary>
    ///     Update a pull request with new data.
    /// </summary>
    /// <param name="pullRequestUri">URI of pull request to update</param>
    /// <param name="pullRequest">Pull request information to update.</param>
    Task UpdatePullRequestAsync(string pullRequestUri, PullRequest pullRequest);

    /// <summary>
    ///     Delete a Pull Request branch
    /// </summary>
    /// <param name="pullRequestUri">URI of pull request to delete branch for</param>
    Task DeletePullRequestBranchAsync(string pullRequestUri);

    #endregion

    #region Repo/Dependency Operations

    /// <summary>
    /// Reads the nuget config and gets the package source
    /// </summary>
    /// <param name="repoUri">repo to get the version from</param>
    /// <param name="commit">commit sha to query</param>
    /// <returns></returns>
    Task<IEnumerable<string>> GetPackageSourcesAsync(string repoUri, string commit);

    /// <summary>
    ///     Get the list of dependencies in the specified repo and branch/commit
    /// </summary>
    /// <param name="repoUri">Repository to get dependencies from</param>
    /// <param name="branchOrCommit">Commit to get dependencies at</param>
    /// <param name="name">Optional name of specific dependency to get information on</param>
    /// <param name="relativeBasePath">Optional base path within the repo to search from</param>
    /// <returns>Matching dependency information.</returns>
    Task<IEnumerable<DependencyDetail>> GetDependenciesAsync(string repoUri, string branchOrCommit, string name = null, UnixPath relativeBasePath = null);

    /// <summary>
    /// Retrieve the common script files from a remote source.
    /// </summary>
    /// <param name="repoUri">URI of repo containing script files.</param>
    /// <param name="commit">Common to get script files at.</param>
    /// <returns>Script files.</returns>
    Task<List<GitFile>> GetCommonScriptFilesAsync(string repoUri, string commit, LocalPath relativeBasePath = null);

    /// <summary>
    /// Retrieve files from a remote source
    /// </summary>
    /// <param name="repoUri">URI of repo containing script files</param>
    /// <param name="commit">Commit to get files at</param>
    /// <param name="path">Path to get files from</param>
    /// <returns>List of files</returns>
    Task<List<GitFile>> GetFilesAtCommitAsync(string repoUri, string commit, string path);

    /// <summary>
    /// Lists files on a path from a remote source
    /// </summary>
    /// <param name="repoUri">URI of repo containing script files</param>
    /// <param name="commit">Commit to get files at</param>
    /// <param name="path">Path to get files from</param>
    /// <returns>List of files on the specified path</returns>
    Task<List<string>> ListFilesAtCommitAsync(string repoUri, string commit, string path);

    /// <summary>
    ///     Create a new branch in the specified repository.
    /// </summary>
    /// <param name="repoUri">Repository to create a branch in</param>
    /// <param name="baseBranch">Branch to create <paramref name="newBranch"/> off of</param>
    /// <param name="newBranch">New branch name.</param>
    Task CreateNewBranchAsync(string repoUri, string baseBranch, string newBranch);

    /// <summary>
    ///     Delete a branch in a repository.
    /// </summary>
    /// <param name="repoUri">Repository to delete a branch in.</param>
    /// <param name="branch">Branch to delete.</param>
    Task DeleteBranchAsync(string repoUri, string branch);

    /// <summary>
    ///     Finds out whether a branch exists in the target repo.
    /// </summary>
    /// <param name="repoUri">Repository to find the branch in</param>
    /// <param name="branch">Branch to find</param>
    Task<bool> BranchExistsAsync(string repoUri, string branch);

    /// <summary>
    ///     Produces a set of updated dpendencies for a repository
    /// </summary>
    /// <param name="repoUri">Repository to update</param>
    /// <param name="branch">Branch of <paramref name="repoUri"/> to update.</param>
    /// <param name="itemsToUpdate">Dependencies that need updating.</param>
    /// <param name="relativeDependencyBasePath">Relative base path of the dependency files</param>
    Task<List<GitFile>> GetUpdatesAsync(
        string repoUri,
        string branch,
        List<DependencyDetail> itemsToUpdate,
        UnixPath relativeDependencyBasePath = null);

    /// <summary>
    ///     Commits a set of files to a repository
    /// </summary>
    Task CommitUpdatesAsync(
        List<GitFile> filesToCommit,
        string repoUri,
        string branch,
        string message);

    Task CommitUpdatesWithNoCloningAsync(
        List<GitFile> filesToCommit,
        string repoUri,
        string branch,
        string message);

    /// <summary>
    ///     Diff two commits in a repository and return information about them.
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <param name="baseCommit">Base version</param>
    /// <param name="targetCommit">Target version</param>
    /// <returns>Diff information</returns>
    Task<GitDiff> GitDiffAsync(string repoUri, string baseCommit, string targetCommit);

    /// <summary>
    ///     Get the latest commit in a branch
    /// </summary>
    /// <param name="repoUri">Remote repository</param>
    /// <param name="branch">Branch</param>
    /// <returns>Latest commit</returns>
    Task<string> GetLatestCommitAsync(string repoUri, string branch);

    /// <summary>
    ///     Get the commits in a repo on the specific branch 
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <param name="sha">Sha of the commit</param>
    /// <returns>Return the commit matching the specified sha. Null if no commit were found.</returns>
    Task<Commit> GetCommitAsync(string repoUri, string sha);


    /// <summary>
    /// Checks that a repository exists
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <returns>True if the repository exists, false otherwise.</returns>
    Task<bool> RepositoryExistsAsync(string repoUri);

    /// <summary>
    ///     Clone a remote repo.
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <param name="commit">Branch, commit, or tag to checkout</param>
    /// <param name="targetDirectory">Directory to clone the repo to</param>
    /// <param name="checkoutSubmodules">Indicates whether submodules should be checked out as well</param>
    /// <param name="gitDirParent">Location for the .git directory, or null for default</param>
    Task CloneAsync(string repoUri, string commit, string targetDirectory, bool checkoutSubmodules, string gitDirectory);

    /// <summary>
    ///    Comment on an existing pull request
    /// </summary>
    /// <param name="pullRequestUri">Uri of the pull request</param>
    /// <param name="comment">Comment message</param>
    Task CommentPullRequestAsync(string pullRequestUri, string comment);

    /// <summary>
    /// Returns the SourceManifest of a VMR on a given branch
    /// </summary>
    Task<SourceManifest> GetSourceManifestAsync(string vmrUri, string branchOrCommit);

    /// <summary>
    /// Returns the SourceManifest of a VMR on a given commit, using cached data if available
    /// </summary>
    Task<SourceManifest> GetSourceManifestAtCommitAsync(string vmrUri, string commitSha);

    /// <summary>
    /// Returns the list of Source Mappings for a VMR on a given branch
    /// </summary>\
    Task<IReadOnlyCollection<SourceMapping>> GetSourceMappingsAsync(string vmrUri, string branch);

    /// <summary>
    /// Returns the SourceDependency tag from Version.Details.xml from a given repo/branch
    /// </summary>
    Task<SourceDependency> GetSourceDependencyAsync(string repoUri, string branch);

    /// <summary>
    ///     Retrieve the contents of a repository file as a string
    /// </summary>
    /// <param name="filePath">Path to file</param>
    /// <param name="repoUri">Repository URI</param>
    /// <param name="branch">Branch to get file contents from</param>
    /// <returns>File contents or throws on file not found.</returns>
    Task<string> GetFileContentsAsync(string filePath, string repoUri, string branch);

    /// <summary>
    /// Returns a list of tree names (directories) under a given path in a given branch
    /// </summary>
    /// <param name="path"></param>
    /// <param name="repoUri"></param>
    /// <param name="branch"></param>
    /// <returns></returns>
    Task<IReadOnlyCollection<string>> GetGitTreeNames(string path, string repoUri, string branch);

    #endregion
}
