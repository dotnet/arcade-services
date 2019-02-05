// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib
{
    public class DependencyGraphNodeComparer : IEqualityComparer<DependencyGraphNode>
    {
        public bool Equals(DependencyGraphNode x, DependencyGraphNode y)
        {
            return x.Commit == y.Commit &&
                   x.RepoUri == y.RepoUri;
        }

        public int GetHashCode(DependencyGraphNode obj)
        {
            return (obj.Commit, obj.RepoUri).GetHashCode();
        }
    }
}
