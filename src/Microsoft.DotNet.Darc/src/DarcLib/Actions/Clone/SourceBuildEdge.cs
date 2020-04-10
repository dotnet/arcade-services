// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class SourceBuildEdge
    {
        public static IEqualityComparer<SourceBuildEdge> InOutComparer { get; } =
            new InOutComparerImplementation();

        /// <summary>
        /// The upstream identity, the dependency.
        /// </summary>
        public SourceBuildIdentity Upstream { get; set; }

        /// <summary>
        /// The downstream identity, the parent in dependency terms.
        /// </summary>
        public SourceBuildIdentity Downstream { get; set; }

        /// <summary>
        /// The source of this identity, or null if this didn't come from a DarcLib dependency.
        /// </summary>
        public DependencyDetail Source { get; set; }

        public bool ProductCritical { get; set; }

        /// <summary>
        /// Marks this link was discovered in the first wave where the target node existed. That is,
        /// the target node wasn't already in the graph when the parent node saw it. This influences
        /// skip check behavior, so is useful to show in diagnostic output.
        ///
        /// For example, circular-by-name-only dependencies are checked for each node only when that
        /// node first enters a graph, so another circle might show up later that we "miss". (This
        /// check is a heuristic for performance, so it isn't a problem.)
        /// </summary>
        public bool FirstDiscoverer { get; set; }

        public SkipDependencyExplorationExplanation SkippedReason { get; set; }

        public SourceBuildIdentity OveriddenUpstreamForCoherency { get; set; }

        public override string ToString() => $"-> {Upstream}";

        public string ToConflictExplanationString() => $"-> {Upstream} for '{Source?.Name}' '{Source?.Version}'";

        public SourceBuildEdge CreateShallowCopy() => (SourceBuildEdge)MemberwiseClone();

        private class InOutComparerImplementation : IEqualityComparer<SourceBuildEdge>
        {
            public bool Equals(SourceBuildEdge x, SourceBuildEdge y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return 
                    SourceBuildIdentity.CaseInsensitiveComparer.Equals(x.Upstream, y.Upstream) &&
                    SourceBuildIdentity.CaseInsensitiveComparer.Equals(x.Downstream, y.Downstream);
            }

            public int GetHashCode(SourceBuildEdge obj) =>
                (
                    SourceBuildIdentity.CaseInsensitiveComparer.GetHashCode(obj.Upstream),
                    SourceBuildIdentity.CaseInsensitiveComparer.GetHashCode(obj.Downstream)
                ).GetHashCode();
        }
    }
}
