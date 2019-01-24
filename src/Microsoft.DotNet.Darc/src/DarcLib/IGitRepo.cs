// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    public interface IGitRepo
    {
        /// <summary>
        /// Create a new branch in a repository
        /// </summary>
        /// <param name="repoUri">Repo to create a branch in</param>
        /// <param name="newBranch">New branch name</param>
        /// <param name="baseBranch">Base of new branch</param>
        Task CreateBranchAsync(string repoUri, string newBranch, string baseBranch);

        /// <summary>
        /// Delete a branch from a repository
        /// </summary>
        /// <param name="repoUri">Repository where the branch lives</param>
        /// <param name="branch">The branch to delete</param>
        Task DeleteBranchAsync(string repoUri, string branch);

        /// <summary>
        ///     Commit or update a set of files to a repo
        /// </summary>
        /// <param name="filesToCommit">Files to comit</param>
        /// <param name="repoUri">Remote repository URI</param>
        /// <param name="branch">Branch to push to</param>
        /// <param name="commitMessage">Commit message</param>
        /// <returns></returns>
        Task CommitFilesAsync(List<GitFile> filesToCommit, string repoUri, string branch, string commitMessage);

        /// <summary>
        ///     Search pull requests matching the specified criteria
        /// </summary>
        /// <param name="repoUri">URI of repo containing the pull request</param>
        /// <param name="pullRequestBranch">Source branch for PR</param>
        /// <param name="status">Current PR status</param>
        /// <param name="keyword">Keyword</param>
        /// <param name="author">Author</param>
        /// <returns>List of pull requests matching the specified criteria</returns>
        Task<IEnumerable<int>> SearchPullRequestsAsync(
            string repoUri,
            string pullRequestBranch,
            PrStatus status,
            string keyword = null,
            string author = null);

        /// <summary>
        /// Get the status of a pull request
        /// </summary>
        /// <param name="pullRequestUrl">URI of pull request</param>
        /// <returns>Pull request status</returns>
        Task<PrStatus> GetPullRequestStatusAsync(string pullRequestUrl);

        /// <summary>
        ///     Retrieve information on a specific pull request
        /// </summary>
        /// <param name="pullRequestUrl">Uri of the pull request</param>
        /// <returns>Information on the pull request.</returns>
        Task<PullRequest> GetPullRequestAsync(string pullRequestUrl);

        /// <summary>
        ///     Create a new pull request for a repository
        /// </summary>
        /// <param name="repoUri">Repo to create the pull request for.</param>
        /// <param name="pullRequest">Pull request data</param>
        /// <returns></returns>
        Task<string> CreatePullRequestAsync(string repoUri, PullRequest pullRequest);

        /// <summary>
        ///     Update a pull request with new information
        /// </summary>
        /// <param name="pullRequestUri">Uri of pull request to update</param>
        /// <param name="pullRequest">Pull request info to update</param>
        /// <returns></returns>
        Task UpdatePullRequestAsync(string pullRequestUri, PullRequest pullRequest);

        /// <summary>
        ///     Merge a pull request
        /// </summary>
        /// <param name="pullRequestUrl">Uri of pull request to merge</param>
        /// <param name="parameters">Settings for merge</param>
        /// <returns></returns>
        Task MergePullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters);

        /// <summary>
        ///     Create a new comment, or update the last comment with an updated message,
        ///     if that comment was created by Darc.
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request</param>
        /// <param name="message">Message to post</param>
        Task CreateOrUpdatePullRequestCommentAsync(string pullRequestUrl, string message);

        /// <summary>
        ///     Retrieve a set of file under a specific path at a commit
        /// </summary>
        /// <param name="repoUri">Repository URI</param>
        /// <param name="commit">Commit to get files at</param>
        /// <param name="path">Path to retrieve files from</param>
        /// <returns>Set of files under <paramref name="path"/> at <paramref name="commit"/></returns>
        Task<List<GitFile>> GetFilesAtCommitAsync(string repoUri, string commit, string path);

        /// <summary>
        ///     Retrieve the contents of a repository file as a string
        /// </summary>
        /// <param name="filePath">Path to file</param>
        /// <param name="repoUri">Repository URI</param>
        /// <param name="branch">Branch to get file contents from</param>
        /// <returns>File contents.</returns>
        Task<string> GetFileContentsAsync(string filePath, string repoUri, string branch);

        /// <summary>
        ///     Get the latest commit in a repo on the specific branch 
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="branch">Branch to retrieve the latest sha for</param>
        /// <returns>Latest sha.  Throws if no commits were found.</returns>
        Task<string> GetLastCommitShaAsync(string repoUri, string branch);

        /// <summary>
        ///     Determine whether a file exists in a repo at a specified branch and
        ///     returns the SHA of the file if it does.
        /// </summary>
        /// <param name="repoUri">Repository URI</param>
        /// <param name="filePath">Path to file</param>
        /// <param name="branch">Branch</param>
        /// <returns>Sha of file or empty string if the file does not exist.</returns>
        Task<string> CheckIfFileExistsAsync(string repoUri, string filePath, string branch);

        /// <summary>
        /// Retrieve the list of status checks on a PR.
        /// </summary>
        /// <param name="pullRequestUrl">Uri of pull request</param>
        /// <returns>List of status checks.</returns>
        Task<IList<Check>> GetPullRequestChecksAsync(string pullRequestUrl);
    }

    public class PullRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string BaseBranch { get; set; }
        public string HeadBranch { get; set; }
    }
}
