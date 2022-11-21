// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib;

public interface IGitRepoCloner
{
    /// <summary>
    ///     Clone a remote repository.
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <param name="commit">Branch, commit, or tag to checkout</param>
    /// <param name="targetDirectory">Directory to clone to</param>
    /// <param name="checkoutSubmodules">Indicates whether submodules should be checked out as well</param>
    /// <param name="gitDirectory">Location for .git directory, or null for default</param>
    void Clone(string repoUri, string commit, string targetDirectory, bool checkoutSubmodules, string gitDirectory = null);
}
