// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib
{
    public interface ILocalGitRepo : IGitRepo
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
        /// Returns a list of git submodules registered in a given repository.
        /// </summary>
        /// <param name="repoDir">Path to a git repository</param>
        /// <param name="commit">Which commit the info is retrieved for</param>
        List<GitSubmoduleInfo> GetGitSubmodules(string repoDir, string commit);
    }
}
