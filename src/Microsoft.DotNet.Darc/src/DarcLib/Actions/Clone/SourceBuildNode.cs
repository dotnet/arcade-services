// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Models.Darc;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    /// <summary>
    /// A repo that has had its Version.Details.xml file evaluated during a darc clone operation.
    /// </summary>
    public class SourceBuildNode
    {
        public static IEqualityComparer<SourceBuildNode> CaseInsensitiveComparer { get; } =
            new CaseInsensitiveComparerImplementation();

        public SourceBuildIdentity Identity { get; set; }

        public IEnumerable<DarcCloneOverrideDetail> Overrides { get; set; }

        public IEnumerable<SourceBuildEdge> UpstreamEdges { get; set; }

        public override string ToString() => $"Node {Identity}";

        private class CaseInsensitiveComparerImplementation : IEqualityComparer<SourceBuildNode>
        {
            public bool Equals(SourceBuildNode x, SourceBuildNode y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return SourceBuildIdentity.CaseInsensitiveComparer.Equals(x.Identity, y.Identity);
            }

            public int GetHashCode(SourceBuildNode obj) =>
                SourceBuildIdentity.CaseInsensitiveComparer.GetHashCode(obj.Identity);
        }
    }
}
