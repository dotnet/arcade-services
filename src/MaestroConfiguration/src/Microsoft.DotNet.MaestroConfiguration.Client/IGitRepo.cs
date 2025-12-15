// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

public interface IGitRepo
{
    /// <summary>
    /// Checks if a repository exists at the specified location.
    /// </summary>
    /// <param name="repositoryUri">The URI or local path of the repository to check.</param>
    /// <returns><c>true</c> if the repository exists; otherwise, <c>false</c>.</returns>
    Task<bool> RepoExistsAsync(string repositoryUri);

    /// <summary>
    /// Checks if a branch exists in the specified repository.
    /// </summary>
    /// <param name="repositoryUri">The URI or local path of the repository.</param>
    /// <param name="branchName">The name of the branch to check.</param>
    /// <returns><c>true</c> if the branch exists; otherwise, <c>false</c>.</returns>
    Task<bool> DoesBranchExistAsync(string repositoryUri, string branchName);

    /// <summary>
    /// Commits a set of files to a branch in the specified repository.
    /// </summary>
    /// <param name="repositoryUri">The URI or local path of the repository.</param>
    /// <param name="branchName">The name of the branch to commit to.</param>
    /// <param name="files">The collection of files to commit.</param>
    /// <param name="commitMessage">The commit message.</param>
    Task CommitFilesAsync(string repositoryUri, string branchName, IReadOnlyList<GitFile> files, string commitMessage);

    /// <summary>
    /// Creates a new branch in the specified repository.
    /// </summary>
    /// <param name="repositoryUri">The URI or local path of the repository.</param>
    /// <param name="branch">The name of the new branch to create.</param>
    /// <param name="baseBranch">The name of the branch to base the new branch on.</param>
    Task CreateBranchAsync(string repositoryUri, string branch, string baseBranch);

    /// <summary>
    /// Gets the contents of a file from the specified repository and branch.
    /// </summary>
    /// <param name="repositoryUri">The URI or local path of the repository.</param>
    /// <param name="configurationBranch">The branch to read the file from.</param>
    /// <param name="filePath">The path to the file within the repository.</param>
    /// <returns>The contents of the file as a string.</returns>
    /// <exception cref="FileNotFoundInRepoException">Thrown when specified file is not found in the repository.</exception>
    Task<string> GetFileContentsAsync(string repositoryUri, string configurationBranch, string filePath);

    /// <summary>
    /// Creates a pull request in the specified repository.
    /// </summary>
    /// <param name="repositoryUri">The URI or local path of the repository.</param>
    /// <param name="headBranch">The branch containing the changes.</param>
    /// <param name="baseBranch">The branch to merge changes into.</param>
    /// <param name="prTitle">The title of the pull request.</param>
    /// <param name="prDescription">An optional description for the pull request.</param>
    /// <returns>The URL or identifier of the created pull request.</returns>
    Task<string> CreatePullRequestAsync(string repositoryUri, string headBranch, string baseBranch, string prTitle, string? prDescription = null);

    Task<List<GitFile>> GetFilesContentAsync(string repositoryUri, string branch, string path);

    Task DeleteFileAsync(string repositoryUri, string branch, string filePath, string commitMessage);

    Task <List<string>> ListBlobsAsync(string repositoryUri, string branch, string path);
}
