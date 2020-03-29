// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class SourceBuildGraph
    {
        public static SourceBuildGraph Create(
            Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> upstreamMap)
        {
            // Flip the upstream graph for ascending lookups.
            var downstreamMap = upstreamMap
                .SelectMany(entry => entry.Value.Select(dep => new
                {
                    Repo = entry.Key,
                    Downstream = dep
                }))
                .GroupBy(v => v.Downstream, v => v.Repo, SourceBuildIdentity.CaseInsensitiveComparer)
                .ToDictionary(v => v.Key, v => v.ToArray());

            return new SourceBuildGraph(
                downstreamMap.Keys
                    .Union(upstreamMap.Keys, SourceBuildIdentity.CaseInsensitiveComparer).ToArray(),
                upstreamMap,
                downstreamMap);
        }

        public IReadOnlyList<SourceBuildIdentity> Nodes { get; }

        private Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> Upstreams { get; }
        private Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> Downstreams { get; }

        private SourceBuildGraph(
            IReadOnlyList<SourceBuildIdentity> nodes,
            Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> upstreams,
            Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> downstreams)
        {
            Nodes = nodes;
            Upstreams = upstreams;
            Downstreams = downstreams;
        }

        public IEnumerable<SourceBuildIdentity> GetAllDownstreams(SourceBuildIdentity node) =>
            GetTraverseListCore(node, Downstreams);

        public IEnumerable<SourceBuildIdentity> GetAllUpstreams(SourceBuildIdentity node) =>
            GetTraverseListCore(node, Upstreams);

        public IEnumerable<SourceBuildIdentity> GetTraverseListCore(
            SourceBuildIdentity start,
            Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> links)
        {
            var visited = new HashSet<SourceBuildIdentity>(SourceBuildIdentity.CaseInsensitiveComparer);
            var next = new Queue<SourceBuildIdentity>();
            next.Enqueue(start);

            while (next.Any())
            {
                SourceBuildIdentity node = next.Dequeue();

                if (!links.TryGetValue(node, out var linkedNodes))
                {
                    continue;
                }

                foreach (var linkedNode in linkedNodes)
                {
                    if (!visited.Add(linkedNode))
                    {
                        continue;
                    }

                    yield return linkedNode;
                    next.Enqueue(linkedNode);
                }
            }
        }
    }
}
