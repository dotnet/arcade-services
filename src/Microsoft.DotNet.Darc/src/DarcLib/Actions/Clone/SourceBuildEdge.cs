// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class SourceBuildEdge
    {
        /// <summary>
        /// The downstream identity, the "parent" in dependency terms.
        /// </summary>
        public SourceBuildIdentity Downstream { get; set; }

        /// <summary>
        /// The upstream identity, the dependency.
        /// </summary>
        public SourceBuildIdentity Upstream { get; set; }

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
    }
}
