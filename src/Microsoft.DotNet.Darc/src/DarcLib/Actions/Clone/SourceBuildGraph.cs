// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class SourceBuildGraph
    {
        public static SourceBuildGraph Create(IEnumerable<SourceBuildNode> nodes)
        {
            var graph = new SourceBuildGraph
            {
            };

            graph.SetNodes(nodes.ToArray());

            // Some nodes have upstreams that we can't find nodes for.
            graph.IdentitiesWithoutNodes = graph.Nodes
                .SelectMany(
                    n => n.UpstreamEdges.NullAsEmpty()
                        .Where(u => !graph.IdentityNodes.ContainsKey(u.Upstream))
                        .Select(u => new SourceBuildNode
                        {
                            Identity = u.Upstream,
                            UpstreamEdges = Enumerable.Empty<SourceBuildEdge>()
                        }))
                .Distinct(SourceBuildNode.CaseInsensitiveComparer)
                .ToArray();

            if (graph.IdentitiesWithoutNodes.Any())
            {
                // Generate nodes if any are missing to make graph operations simpler.
                graph.SetNodes(graph.Nodes.Concat(graph.IdentitiesWithoutNodes).ToArray());
            }

            return graph;
        }

        public IReadOnlyList<SourceBuildNode> Nodes { get; set; }

        public IEnumerable<SourceBuildEdge> AllEdges => Nodes.SelectMany(n => n.UpstreamEdges.NullAsEmpty());

        public Dictionary<SourceBuildIdentity, SourceBuildNode> IdentityNodes { get; set; }

        /// <summary>
        /// Keep a fast lookup of all edges that have a specified identity as its downstream. You
        /// can get from a node to all of its upstream edges, and this allows the opposite.
        /// </summary>
        public Dictionary<SourceBuildIdentity, SourceBuildEdge[]> EdgesWithUpstream { get; set; }

        /// <summary>
        /// Some identities were intentionally unexplored. Keep track here, to display later if
        /// necessary for diagnostics.
        /// </summary>
        public IReadOnlyList<SourceBuildNode> IdentitiesWithoutNodes { get; set; }

        private void SetNodes(IReadOnlyList<SourceBuildNode> nodes)
        {
            Nodes = nodes;

            IdentityNodes = Nodes.ToDictionary(
                n => n.Identity,
                n => n,
                SourceBuildIdentity.CaseInsensitiveComparer);

            EdgesWithUpstream = Nodes
                .SelectMany(n => n.UpstreamEdges.NullAsEmpty())
                .GroupBy(e => e.Upstream)
                .ToDictionary(
                    e => e.Key,
                    e => e.ToArray());
        }

        public string ToGraphVizString()
        {
            var sb = new StringBuilder("digraph G {\n");

            sb.AppendLine("rankdir=LR");
            sb.AppendLine("node [shape=box color=\"lightsteelblue1\" style=filled]");

            IEnumerable<string> GetNodeAttributes(SourceBuildNode node)
            {
                yield break;
                //if (node.SkippedReason != null)
                //{
                //    yield return $"label=\"{node.Identity}\\n{node.SkippedReason.Reason}\"";
                //}
                //if (node.SkippedReason?.ToGraphVizColor() is string color)
                //{
                //    yield return $"color=\"{color}\"";
                //    yield return $"fillcolor=\"{color}\"";
                //}
            }

            IEnumerable<string> GetEdgeAttributes(SourceBuildNode source, SourceBuildNode node)
            {
                yield break;
                //if (productCritical.Contains(node))
                //{
                //    yield return "color=\"green\"";
                //    yield return "penwidth=3";
                //}
                //else
                //{
                //    if (node.SkippedReason == null)
                //    {
                //        yield return "penwidth=2";
                //    }
                //    if (node.SkippedReason?.ToGraphVizColor() is string color)
                //    {
                //        yield return $"color=\"{color}\"";
                //    }
                //}

                //if (!source.FirstDiscovererOfUpstreams.Contains(node.Identity))
                //{
                //    yield return "style=dashed";
                //}
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

            sb.Append("root[shape=circle fillcolor=\"chartreuse\"]\nroot -> {");
            foreach (var n in Nodes.Where(n => !GetDownstreams(n.Identity).Any()))
            {
                AppendNode(n);
                sb.Append(";");
            }
            sb.AppendLine("}");

            foreach (var n in Nodes)
            {
                AppendNode(n);
                AppendAttributes(GetNodeAttributes(n));

                SourceBuildNode[] upstreams = GetUpstreams(n.Identity).ToArray();
                if (upstreams.Any())
                {
                    foreach (var u in upstreams.Select(u => u.Identity).Distinct(SourceBuildIdentity.CaseInsensitiveComparer))
                    {
                        // Don't use grouping (A -> { B C }) so that we can apply attributes to each
                        // individual link.
                        sb.AppendLine();
                        sb.Append("\"");
                        sb.Append(n.Identity);
                        sb.Append("\" -> ");
                        AppendNode(IdentityNodes[u]);
                        AppendAttributes(GetEdgeAttributes(n, IdentityNodes[u]));
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        public IEnumerable<SourceBuildNode> GetDownstreams(SourceBuildIdentity node) =>
            EdgesWithUpstream.TryGetValue(node, out var edges)
            ? edges.Select(e => IdentityNodes[e.Upstream])
            : Enumerable.Empty<SourceBuildNode>();

        public IEnumerable<SourceBuildNode> GetUpstreams(SourceBuildIdentity node) =>
            IdentityNodes[node].UpstreamEdges.NullAsEmpty().Select(e => IdentityNodes[e.Upstream]);

        public IEnumerable<SourceBuildNode> GetAllDownstreams(SourceBuildIdentity node) =>
            GetTraverseListCore(node, GetDownstreams);

        public IEnumerable<SourceBuildNode> GetAllUpstreams(SourceBuildIdentity node) =>
            GetTraverseListCore(node, GetUpstreams);

        public IEnumerable<SourceBuildNode> GetRootNodes() =>
            Nodes.Where(n => !GetDownstreams(n.Identity).Any());

        private IEnumerable<SourceBuildNode> GetTraverseListCore(
            SourceBuildIdentity start,
            Func<SourceBuildIdentity, IEnumerable<SourceBuildNode>> links)
        {
            var visited = new HashSet<SourceBuildIdentity>();
            var next = new Queue<SourceBuildIdentity>();
            next.Enqueue(start);

            while (next.Any())
            {
                SourceBuildIdentity node = next.Dequeue();

                foreach (var linkedNode in links(node))
                {
                    if (visited.Add(linkedNode.Identity))
                    {
                        yield return linkedNode;
                        next.Enqueue(linkedNode.Identity);
                    }
                }
            }
        }
    }
}
