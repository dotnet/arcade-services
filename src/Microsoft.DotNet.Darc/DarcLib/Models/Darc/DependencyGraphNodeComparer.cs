// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

public class DependencyGraphNodeComparer : IEqualityComparer<DependencyGraphNode>
{
    public bool Equals(DependencyGraphNode x, DependencyGraphNode y)
    {
        return x.Commit == y.Commit &&
               x.Repository == y.Repository;
    }

    public int GetHashCode(DependencyGraphNode obj)
    {
        return (obj.Commit, obj.Repository).GetHashCode();
    }
}
