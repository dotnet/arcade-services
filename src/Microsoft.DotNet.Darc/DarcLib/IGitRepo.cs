// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace Microsoft.DotNet.DarcLib;

public interface IGitRepo
{
    /// <summary>
    ///     Retrieve the contents of a repository file as a string
    /// </summary>
    /// <param name="filePath">Path to file</param>
    /// <param name="repoUri">Repository URI</param>
    /// <param name="branch">Branch to get file contents from</param>
    /// <returns>File contents or throws on file not found.</returns>
    Task<string> GetFileContentsAsync(string filePath, string repoUri, string branch);

    /// <summary>
    ///     Commit or update a set of files to a repo
    /// </summary>
    /// <param name="filesToCommit">Files to comit</param>
    /// <param name="repoUri">Remote repository URI</param>
    /// <param name="branch">Branch to push to</param>
    /// <param name="commitMessage">Commit message</param>
    Task CommitFilesAsync(List<GitFile> filesToCommit, string repoUri, string branch, string commitMessage);

    /// <summary>
    ///     Finds out whether a branch exists in the target repo.
    /// </summary>
    /// <param name="repoUri">Repository to find the branch in</param>
    /// <param name="branch">Branch to find</param>
    Task<bool> DoesBranchExistAsync(string repoUri, string branch);

    /// <summary>
    /// Checks that a repository exists
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <returns>True if the repository exists, false otherwise.</returns>
    Task<bool> RepoExistsAsync(string repoUri);

    /// <summary>
    /// Create a new branch in a repository
    /// </summary>
    /// <param name="repoUri">Repo to create a branch in</param>
    /// <param name="newBranch">New branch name</param>
    /// <param name="baseBranch">Base of new branch</param>
    Task CreateBranchAsync(string repoUri, string newBranch, string baseBranch);

    Task<List<GitTreeItem>> LsTreeAsync(string uri, string gitRef, string path = null);

    async Task<bool> IsRepoVmrAsync(string repoUri, string branch)
    {
        try
        {
            await GetFileContentsAsync(VmrInfo.DefaultRelativeSourceMappingsPath, repoUri, branch);
            return true;
        }
        catch (DependencyFileNotFoundException)
        {
            return false;
        }
    }
}
