// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;

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
}
