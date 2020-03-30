// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
                    .Union(upstreamMap.Keys, SourceBuildIdentity.CaseInsensitiveComparer)
                    .OrderBy(n => n.RepoUri, StringComparer.Ordinal)
                    .ToArray(),
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

        /// <summary>
        /// Create an artificially coherent graph: only keep one commit of each repo by name. For
        /// each identity node, the latest version is kept and dependenices on all versions are
        /// redirected to the kept version.
        ///
        /// If a node has no version information (no DependencyDetail) we assume it is the latest.
        /// This should only be the case when the user manually passes in a url and commit hash. If
        /// multiple nodes with the same name lack version information, throws an exception.
        /// </summary>
        public SourceBuildGraph CreateArtificiallyCoherentGraph()
        {
            // Map old node => new node.
            Dictionary<SourceBuildIdentity, SourceBuildIdentity> newNodes = Nodes
                .GroupBy(n => n, SourceBuildIdentity.RepoNameOnlyComparer)
                .Select(group =>
                    group.SingleOrDefault(g => g.Source == null) ??
                    group.OrderByDescending(n => NuGetVersion.Parse(n.Source.Version)).First())
                .ToDictionary(
                    n => n,
                    n => n,
                    SourceBuildIdentity.RepoNameOnlyComparer);

            Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> newUpstreamMap = Upstreams
                .GroupBy(
                    pair => newNodes[pair.Key],
                    // Transform all upstream nodes into the merged node, and dedup.
                    pair => pair.Value.Select(u => newNodes[u]).Distinct().ToArray())
                .ToDictionary(
                    group => group.Key,
                    // Combine all upstream lists for this merged node, and dedup.
                    group => group.SelectMany(upstreams => upstreams).Distinct().ToArray());

            return Create(newUpstreamMap);
        }

        public string ToGraphVizString()
        {
            var sb = new StringBuilder("digraph G { rankdir=LR;");

            foreach (var n in Nodes)
            {
                sb.Append("\"");
                sb.Append(n);
                sb.Append("\"");

                if (Upstreams.TryGetValue(n, out var upstreams))
                {
                    sb.Append(" -> {");
                    foreach (var u in upstreams)
                    {
                        sb.Append("\"");
                        sb.Append(u);
                        sb.Append("\";");
                    }
                    sb.Append("}");
                }

                sb.AppendLine();
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        public IEnumerable<SourceBuildIdentity> GetDownstreams(SourceBuildIdentity node) =>
            Downstreams.TryGetValue(node, out var values)
                ? values
                : Enumerable.Empty<SourceBuildIdentity>();

        public IEnumerable<SourceBuildIdentity> GetUpstreams(SourceBuildIdentity node) =>
            Upstreams.TryGetValue(node, out var values)
                ? values
                : Enumerable.Empty<SourceBuildIdentity>();

        public IEnumerable<SourceBuildIdentity> GetAllDownstreams(SourceBuildIdentity node) =>
            GetTraverseListCore(node, Downstreams);

        public IEnumerable<SourceBuildIdentity> GetAllUpstreams(SourceBuildIdentity node) =>
            GetTraverseListCore(node, Upstreams);

        private IEnumerable<SourceBuildIdentity> GetTraverseListCore(
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
