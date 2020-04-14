// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

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

        public bool ExcludeFromSourceBuild { get; set; }

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

        public SkipDependencyExplorationExplanation GetExplorationSkipReason(SourceBuildGraph graph)
        {
            if (graph.Nodes.Except(graph.IdentitiesWithoutNodes).Any(
                n => SourceBuildIdentity.CaseInsensitiveComparer.Equals(n.Identity, Upstream)))
            {
                return new SkipDependencyExplorationExplanation
                {
                    Reason = SkipDependencyExplorationReason.AlreadyVisited
                };
            }

            if (ExcludeFromSourceBuild)
            {
                return new SkipDependencyExplorationExplanation
                {
                    Reason = SkipDependencyExplorationReason.ExcludedFromSourceBuild
                };
            }

            // Remove self-dependency. E.g. arcade depends on previous versions of itself to
            // build, so this tends to go on essentially forever.
            if (string.Equals(
                Upstream.RepoUri,
                Downstream.RepoUri,
                StringComparison.OrdinalIgnoreCase))
            {
                return new SkipDependencyExplorationExplanation
                {
                    Reason = SkipDependencyExplorationReason.SelfDependency,
                    Details =
                        $"Skipping self-dependency in {Downstream.RepoUri} " +
                        $"({Downstream.Commit} => {Upstream.Commit})"
                };
            }
            // Remove circular dependencies that have different hashes. That is, detect
            // circular-by-name-only dependencies.
            // e.g. DotNet-Trusted -> core-setup -> DotNet-Trusted -> ...
            // We are working our way upstream, so this check walks all downstreams we've
            // seen so far to see if any have this potential repo name. (We can't simply
            // check if we've seen the repo name before: other branches may have the same
            // repo name dependency but not as part of a circular dependency.)
            var allDownstreams = graph.GetAllDownstreams(Upstream).ToArray();
            if (allDownstreams.Any(
                d => SourceBuildIdentity.RepoNameOnlyComparer.Equals(d.Identity, Upstream)))
            {
                return new SkipDependencyExplorationExplanation
                {
                    Reason = SkipDependencyExplorationReason.CircularWhenOnlyConsideringName,
                    Details =
                        "Skipping already-seen circular dependency " +
                        $"from {Downstream} to {Upstream}\n" +
                        string.Join(" -> ", allDownstreams.Select(d => d.ToString()))
                };
            }
            // Remove repos with invalid dependency info: missing commit.
            if (string.IsNullOrWhiteSpace(Upstream.Commit))
            {
                return new SkipDependencyExplorationExplanation
                {
                    Reason = SkipDependencyExplorationReason.DependencyDetailMissingCommit,
                    Details =
                        $"Skipping dependency from {Downstream} to {Upstream.RepoUri}: " +
                        "Missing commit."
                };
            }

            return null;
        }

        public override string ToString() => $"-> {Upstream}";

        public string ToConflictExplanationString() =>
            $"Critical: {Downstream} -> {OveriddenUpstreamForCoherency ?? Upstream} " +
            $"for '{Source?.Name}' '{Source?.Version}'";

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
