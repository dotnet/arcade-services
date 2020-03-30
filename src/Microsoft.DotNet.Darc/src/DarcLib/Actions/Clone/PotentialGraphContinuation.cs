// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class PotentialGraphContinuation
    {
        /// <summary>
        /// The upstream under consideration to evaluate in the next phase.
        /// </summary>
        public SourceBuildIdentity Upstream { get; }

        /// <summary>
        /// The repository that has the upstream.
        /// </summary>
        public SourceBuildIdentity Source { get; }

        /// <summary>
        /// The partial graph discovered so far. Downstreams are evaluated, but not upstreams.
        /// </summary>
        public SourceBuildGraph PartialGraph { get; }

        public PotentialGraphContinuation(
            SourceBuildIdentity upstream,
            SourceBuildIdentity source,
            SourceBuildGraph partialGraph)
        {
            Upstream = upstream;
            Source = source;
            PartialGraph = partialGraph;
        }

        /// <summary>
        /// Detect the absense of circular dependencies even if they have different commit hashes.
        /// That is, detect circular-by-name-only dependencies. Inverted result is intended for use
        /// as a custom filter.
        ///
        /// e.g. DotNet-Trusted -> core-setup -> DotNet-Trusted -> ...
        /// 
        /// We are working our way upstream, so this check walks all downstreams we've seen so far
        /// to see if any have this potential repo name. (We can't simply check if we've seen the
        /// repo name before: other branches may have the same repo name dependency but not as part
        /// of a circular dependency.)
        /// </summary>
        public static bool HasNoCircularDependencyDisregardingCommit(PotentialGraphContinuation potential)
        {
            bool downstreamCircularDependency = potential.PartialGraph
                .GetAllDownstreams(potential.Upstream)
                .Any(
                    d => string.Equals(
                        d.RepoUri,
                        potential.Source.RepoUri,
                        StringComparison.OrdinalIgnoreCase));

            return !downstreamCircularDependency;
        }
    }
}
