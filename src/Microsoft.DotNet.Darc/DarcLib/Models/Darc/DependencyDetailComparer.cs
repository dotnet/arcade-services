// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

public class DependencyDetailComparer : IEqualityComparer<DependencyDetail>
{
    public bool Equals(DependencyDetail x, DependencyDetail y)
    {
        return x.Commit == y.Commit &&
               x.Name == y.Name &&
               x.RepoUri == y.RepoUri &&
               x.Version == y.Version &&
               x.Type == y.Type;
    }

    public int GetHashCode(DependencyDetail obj)
    {
        return (obj.Commit,
            obj.Name,
            obj.RepoUri,
            obj.Version,
            obj.Type).GetHashCode();
    }
}
