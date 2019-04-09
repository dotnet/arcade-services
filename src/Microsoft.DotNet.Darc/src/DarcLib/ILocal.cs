// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    internal interface ILocal
    {
        Task UpdateDependenciesAsync(List<DependencyDetail> dependencies, IRemoteFactory remoteFactory);
        /// <summary>
        ///     Verify the local repository has correct and consistent dependency information
        /// </summary>
        /// <returns>Async task</returns>
        Task<bool> Verify();

        /// <summary>
        ///     Checkout the specified tag, branch, or commit.
        /// </summary>
        /// <param name="commit">Tag, branch, or commit to checkout</param>
        void Checkout(string commit);
    }
}
