// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks
{
    /// <summary>
    ///     Compares ItemsToSign objects based on file
    /// </summary>
    public class ItemsToSignComparer : IEqualityComparer<ItemsToSign>
    {
        public bool Equals(ItemsToSign x, ItemsToSign y)
        {
            return x.Include.Equals(y.Include, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(ItemsToSign obj)
        {
            return (obj.Include).GetHashCode();
        }
    }
}
