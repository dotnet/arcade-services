// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class SourceBuildGraph
    {
        public static SourceBuildGraph Create(
            IEnumerable<SourceBuildNode> nodes,
            IEnumerable<DarcCloneOverrideDetail> globalOverrides)
        {
            var graph = new SourceBuildGraph
            {
                GlobalOverrides = globalOverrides.NullAsEmpty().ToArray()
            };

            graph.SetNodes(nodes.ToArray());

            // Some nodes have upstreams that we can't find nodes for.
            graph.UnexploredIdentities = graph.Nodes
                .SelectMany(
                    n => n.Upstreams
                        .Where(u => !graph.IdentityNodes.ContainsKey(u))
                        .Select(u => new SourceBuildNode
                        {
                            Identity = u,
                            SkippedReason = n.UpstreamSkipReasons.TryGetValue(u, out var reason)
                                ? reason
                                : null
                        }))
                .Distinct(SourceBuildNode.CaseInsensitiveComparer)
                .ToArray();

            if (graph.UnexploredIdentities.Any())
            {
                // Generate nodes if any are missing to make graph operations simpler.
                graph.SetNodes(graph.Nodes.Concat(graph.UnexploredIdentities).ToArray());
            }

            // Flip the upstream graph for ascending lookups.
            graph.Downstreams = graph.Nodes
                .SelectMany(
                    node => node.Upstreams.NullAsEmpty()
                        .Select(
                            upstream => new
                            {
                                Node = graph.IdentityNodes[upstream],
                                Downstream = node
                            }))
                .GroupBy(p => p.Node.Identity, SourceBuildIdentity.CaseInsensitiveComparer)
                .ToDictionary(g => g.First().Node, g => g.Select(n => n.Downstream).ToArray());

            return graph;
        }

        public IReadOnlyList<SourceBuildNode> Nodes { get; set; }

        public Dictionary<SourceBuildIdentity, SourceBuildNode> IdentityNodes { get; set; }

        public Dictionary<SourceBuildNode, SourceBuildNode[]> Downstreams { get; set; }

        public IReadOnlyList<DarcCloneOverrideDetail> GlobalOverrides { get; set; }

        /// <summary>
        /// Some identities were intentionally unexplored. Keep track here, to display later if
        /// necessary for diagnostics.
        /// </summary>
        public IReadOnlyList<SourceBuildNode> UnexploredIdentities { get; set; }

        private void SetNodes(IReadOnlyList<SourceBuildNode> nodes)
        {
            Nodes = nodes;
            IdentityNodes = Nodes.ToDictionary(
                n => n.Identity,
                n => n,
                SourceBuildIdentity.CaseInsensitiveComparer);
        }

        /// <summary>
        /// Returns all nodes where the node has a dependency detail with ProductCritical = true,
        /// taking into account the global overrides. Overrides in each node are not considered:
        /// there may be multiple paths to each node that conflict without a clear way to resolve
        /// them. This means Core-SDK needs to store all overrides for the graph, which seems fine.
        /// </summary>
        public IEnumerable<SourceBuildNode> GetProductCriticalNodes()
        {
            var globalOverrideMap = GlobalOverrides
                .ToDictionary(
                    o => o.Repo,
                    o => o.FindDependencies.ToDictionary(
                        f => f.Name,
                        f => f.ProductCritical,
                        StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);

            return Nodes.Where(n => n.Identity.Sources.NullAsEmpty().Any(s =>
            {
                // If any dependency that led to this node is critical, node critical.
                if (s.ProductCritical)
                {
                    return true;
                }

                // If the dependency is overridden to be critical, node critical.
                return globalOverrideMap.TryGetValue(n.Identity.RepoUri, out var depMap) &&
                    depMap.TryGetValue(s.Name, out bool? critical) &&
                    critical == true;
            }));
        }

        public string ToGraphVizString()
        {
            var sb = new StringBuilder("digraph G {\n");

            sb.AppendLine("rankdir=LR");
            sb.AppendLine("node [shape=box color=\"lightsteelblue1\" style=filled]");

            IEnumerable<string> GetNodeAttributes(SourceBuildNode node)
            {
                if (node.SkippedReason?.ToGraphVizColor() is string color)
                {
                    yield return $"color=\"{color}\"";
                    yield return $"fillcolor=\"{color}\"";
                }
            }

            IEnumerable<string> GetEdgeAttributes(SourceBuildNode node)
            {
                if (node.SkippedReason != null)
                {
                    yield return "style=dotted";
                }
                if (node.SkippedReason?.ToGraphVizColor() is string color)
                {
                    yield return $"color=\"{color}\"";
                }
            }

            void AppendAttributes(IEnumerable<string> attrs)
            {
                var attrsArray = attrs.ToArray();
                if (attrsArray.Any())
                {
                    sb.Append("[");
                    sb.Append(string.Join(",", attrsArray));
                    sb.Append("]");
                }
            }

            void AppendNode(SourceBuildNode n)
            {
                sb.Append("\"");
                sb.Append(n.Identity);
                sb.Append("\"");
            }

            sb.Append("root -> {");
            foreach (var n in Nodes.Where(n => !GetDownstreams(n).Any()))
            {
                AppendNode(n);
                sb.Append(";");
            }
            sb.AppendLine("}");

            foreach (var n in Nodes)
            {
                AppendNode(n);
                AppendAttributes(GetNodeAttributes(n));

                SourceBuildNode[] upstreams = GetUpstreams(n).ToArray();
                if (upstreams.Any())
                {
                    foreach (var u in upstreams)
                    {
                        // Don't use grouping (A -> { B C }) so that we can apply attributes to each
                        // individual link.
                        sb.AppendLine();
                        sb.Append("\"");
                        sb.Append(n.Identity);
                        sb.Append("\" -> ");
                        AppendNode(u);
                        AppendAttributes(GetEdgeAttributes(u));
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        public IEnumerable<SourceBuildNode> GetDownstreams(SourceBuildNode node) =>
            Downstreams.GetOrDefault(node).NullAsEmpty();

        public IEnumerable<SourceBuildNode> GetDownstreams(SourceBuildIdentity node) =>
            GetDownstreams(IdentityNodes[node]);

        public IEnumerable<SourceBuildNode> GetUpstreams(SourceBuildNode node) =>
            node.Upstreams.NullAsEmpty().Select(u => IdentityNodes[u]);

        public IEnumerable<SourceBuildNode> GetUpstreams(SourceBuildIdentity node) =>
            GetUpstreams(IdentityNodes[node]);

        public IEnumerable<SourceBuildNode> GetAllDownstreams(SourceBuildNode node) =>
            GetTraverseListCore(node, GetDownstreams);

        public IEnumerable<SourceBuildNode> GetAllDownstreams(SourceBuildIdentity node) =>
            GetAllDownstreams(IdentityNodes[node]);

        public IEnumerable<SourceBuildNode> GetAllUpstreams(SourceBuildNode node) =>
            GetTraverseListCore(node, GetUpstreams);

        public IEnumerable<SourceBuildNode> GetAllUpstreams(SourceBuildIdentity node) =>
            GetAllUpstreams(IdentityNodes[node]);

        private IEnumerable<SourceBuildNode> GetTraverseListCore(
            SourceBuildNode start,
            Func<SourceBuildNode, IEnumerable<SourceBuildNode>> links)
        {
            var visited = new HashSet<SourceBuildNode>();
            var next = new Queue<SourceBuildNode>();
            next.Enqueue(start);

            while (next.Any())
            {
                SourceBuildNode node = next.Dequeue();

                foreach (var linkedNode in links(node))
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
