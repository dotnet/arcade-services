// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    public interface ILocalGitRepo
    {
        /// <summary>
        ///     Add a remote to a local repo if does not already exist, and attempt to fetch commits.
        /// </summary>
        void AddRemoteIfMissing(string repoDir, string repoUrl);

        /// <summary>
        ///     Checkout the repo to the specified state.
        /// </summary>
        /// <param name="commit">Tag, branch, or commit to checkout.</param>
        void Checkout(string repoDir, string commit, bool force = false);

        /// <summary>
        ///     Updates local copies of the files.
        /// </summary>
        /// <param name="filesToCommit">Files to update locally</param>
        /// <param name="repoUri">Base path of the repo</param>
        /// <param name="branch">Unused</param>
        /// <param name="commitMessage">Unused</param>
        Task CommitFilesAsync(List<GitFile> filesToCommit, string repoUri, string branch, string commitMessage);

        Task<string> GetFileContentsAsync(string relativeFilePath, string repoUri, string branch);

        Task<List<GitFile>> GetFilesAtCommitAsync(string repoUri, string commit, string path);
    }
}
