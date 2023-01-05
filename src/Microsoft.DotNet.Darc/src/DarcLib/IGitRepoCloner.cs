// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface IGitRepoCloner
{
    /// <summary>
    ///     Clone a remote git repo.
    /// </summary>
    /// <param name="repoUri">Repository uri to clone</param>
    /// <param name="commit">Branch, commit, or tag to checkout</param>
    /// <param name="targetDirectory">Target directory to clone to</param>
    /// <param name="checkoutType">Indicates whether working tree and submodules should be checked out</param>
    /// <param name="gitDirectory">Location for the .git directory, or null for default</param>
    public void Clone(string repoUri, string? commit, string targetDirectory, CheckoutType checkoutType, string? gitDirectory);
}
