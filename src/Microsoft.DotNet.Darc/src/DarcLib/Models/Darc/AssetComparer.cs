// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib
{
    /// <summary>
    ///     Compares assets based on name and version.
    /// </summary>
    public class AssetComparer : IEqualityComparer<Asset>
    {
        public bool Equals(Asset x, Asset y)
        {
            return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase) &&
                x.Version == y.Version;
        }

        public bool Equals(Asset x, DependencyDetail y)
        {
            return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase) &&
                x.Version == y.Version;
        }

        public int GetHashCode(Asset obj)
        {
            return (obj.Name, obj.Version).GetHashCode();
        }
    }
}
