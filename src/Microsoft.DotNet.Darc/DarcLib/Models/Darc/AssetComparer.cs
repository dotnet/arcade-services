// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;
using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

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

    public static bool Equals(Asset x, DependencyDetail y)
    {
        return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase) &&
               x.Version == y.Version;
    }

    public int GetHashCode(Asset obj)
    {
        return (obj.Name, obj.Version).GetHashCode();
    }
}
