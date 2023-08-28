// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;

public interface IGitRepoCloner
{
    /// <summary>
    ///     Clone a remote git repo.
    /// </summary>
    /// <param name="repoUri">Repository uri to clone</param>
    /// <param name="commit">Branch, commit, or tag to checkout</param>
    /// <param name="targetDirectory">Target directory to clone to</param>
    /// <param name="checkoutSubmodules">Indicates whether submodules should be checked out as well</param>
    /// <param name="gitDirectory">Location for the .git directory, or null for default</param>
    public Task CloneAsync(
        string repoUri,
        string? commit,
        string targetDirectory,
        bool checkoutSubmodules,
        string? gitDirectory);

    /// <summary>
    ///     Clone a remote git repo without checking out the working tree.
    /// </summary>
    /// <param name="repoUri">Repository uri to clone</param>
    /// <param name="targetDirectory">Target directory to clone to</param>
    /// <param name="gitDirectory">Location for the .git directory, or null for default</param>
    public Task CloneNoCheckoutAsync(string repoUri, string targetDirectory, string? gitDirectory);
}

public enum CheckoutType
{
    CheckoutWithoutSubmodules,
    CheckoutWithSubmodules,
    NoCheckout,
}
