// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    /// <summary>
    ///     This graph build
    /// </summary>
    public class DependencyFlowGraph
    {
        public DependencyFlowGraph(List<DependencyFlowNode> nodes, List<DependencyFlowEdge> edges)
        {
            Nodes = nodes;
            Edges = edges;
        }

        public List<DependencyFlowNode> Nodes { get; set; }
        public List<DependencyFlowEdge> Edges { get; set; }

        public void RemoveNode(DependencyFlowNode node)
        {
            // Remove the node from the list, then remove corresponding edges
            if (Nodes.Remove(node))
            {
                foreach (DependencyFlowEdge incomingEdge in node.IncomingEdges)
                {
                    // Don't use RemoveEdge as it assumes that this node will remain in the graph and
                    // will cause modification of this IncomingEdges collection while iterating
                    incomingEdge.From.OutgoingEdges.Remove(incomingEdge);
                }
                foreach (DependencyFlowEdge outgoingEdge in node.OutgoingEdges)
                {
                    // Don't use RemoveEdge as it assumes that this node will remain in the graph and
                    // will cause modification of this OutgoingEdges collection while iterating
                    DependencyFlowNode targetNode = outgoingEdge.To;
                    outgoingEdge.To.IncomingEdges.Remove(outgoingEdge);
                    // Recalculate the input channels
                    targetNode.InputChannels = new HashSet<string>(targetNode.IncomingEdges.Select(e => e.Subscription.Channel.Name));
                }
            }
        }

        public void RemoveEdge(DependencyFlowEdge edge)
        {
            if (Edges.Remove(edge))
            {
                edge.From.OutgoingEdges.Remove(edge);
                DependencyFlowNode targetNode = edge.To;
                edge.To.IncomingEdges.Remove(edge);
                // Recalculate the input channels
                targetNode.InputChannels = new HashSet<string>(targetNode.IncomingEdges.Select(e => e.Subscription.Channel.Name));
            }
        }

        /// <summary>
        ///     Prune the graph, 
        /// </summary>
        /// <param name="graph"></param>
        /// <param name="isInterestingNode"></param>
        /// <param name="isInterestingEdge"></param>
        public void PruneGraph(Func<DependencyFlowNode, bool> isInterestingNode,
                               Func<DependencyFlowEdge, bool> isInterestingEdge)
        {
            // If nodes or edges are all interesting, then no reason to 
            HashSet<DependencyFlowNode> unreachableNodes = new HashSet<DependencyFlowNode>(Nodes);
            HashSet<DependencyFlowEdge> unreachableEdges = new HashSet<DependencyFlowEdge>(Edges);
            Stack<DependencyFlowNode> nodes = new Stack<DependencyFlowNode>();

            // Walk each root
            foreach (DependencyFlowNode node in Nodes)
            {
                if (!isInterestingNode(node))
                {
                    continue;
                }

                nodes.Push(node);

                while (nodes.Count != 0)
                {
                    DependencyFlowNode currentNode = nodes.Pop();

                    if (!unreachableNodes.Contains(currentNode))
                    {
                        // Nothing to do
                        continue;
                    }
                    unreachableNodes.Remove(currentNode);
                    foreach (var inputEdge in currentNode.IncomingEdges)
                    {
                        if (isInterestingEdge(inputEdge))
                        {
                            unreachableEdges.Remove(inputEdge);
                            // Push the inputs onto the stack.
                            nodes.Push(inputEdge.From);
                        }
                    }
                }
            }

            // Now walk the graph and eliminate any edges or nodes that
            foreach (var node in unreachableNodes)
            {
                RemoveNode(node);
            }

            foreach (var edge in unreachableEdges)
            {
                RemoveEdge(edge);
            }
        }

        public static DependencyFlowGraph Build(
            List<DefaultChannel> defaultChannels,
            List<Subscription> subscriptions)
        {
            // Dictionary of nodes. Key is the repo+branch
            Dictionary<string, DependencyFlowNode> nodes = new Dictionary<string, DependencyFlowNode>(
                StringComparer.OrdinalIgnoreCase);
            List<DependencyFlowEdge> edges = new List<DependencyFlowEdge>();

            // First create all the channel nodes. There may be disconnected
            // nodes in the graph, so we must process all channels and all subscriptions
            foreach (DefaultChannel channel in defaultChannels)
            {
                DependencyFlowNode flowNode = GetOrCreateNode(channel.Repository, channel.Branch, nodes);
                // Add a the output mapping.
                flowNode.OutputChannels.Add(channel.Channel.Name);
            }

            // Process all subscriptions (edges)
            foreach (Subscription subscription in subscriptions)
            {
                // Get the target of the subscription
                DependencyFlowNode destinationNode = GetOrCreateNode(subscription.TargetRepository, subscription.TargetBranch, nodes);
                // Add the input channel for the node
                destinationNode.InputChannels.Add(subscription.Channel.Name);
                // Translate the input channel + repo to a default channel,
                // and if one is found, an input node.
                IEnumerable<DefaultChannel> inputDefaultChannels = defaultChannels.Where(d => d.Channel.Name == subscription.Channel.Name &&
                                                               d.Repository.Equals(subscription.SourceRepository, StringComparison.OrdinalIgnoreCase));
                foreach (DefaultChannel defaultChannel in inputDefaultChannels)
                {
                    DependencyFlowNode sourceNode = GetOrCreateNode(defaultChannel.Repository, defaultChannel.Branch, nodes);

                    DependencyFlowEdge newEdge = new DependencyFlowEdge(sourceNode, destinationNode, subscription);
                    destinationNode.IncomingEdges.Add(newEdge);
                    sourceNode.OutgoingEdges.Add(newEdge);
                    edges.Add(newEdge);
                }
            }

            return new DependencyFlowGraph(nodes.Select(kv => kv.Value).ToList(), edges);
        }

        private static string NormalizeBranch(string branch)
        {
            // Normalize branch names. Branch names may have "refs/heads" prepended.
            // Remove if they do.
            const string refsHeadsPrefix = "refs/heads/";
            string normalizedBranch = branch;
            if (normalizedBranch.StartsWith("refs/heads/"))
            {
                normalizedBranch = normalizedBranch.Substring(refsHeadsPrefix.Length);
            }

            return normalizedBranch;
        }

        private static DependencyFlowNode GetOrCreateNode(
            string repo,
            string branch,
            Dictionary<string, DependencyFlowNode> nodes)
        {
            string normalizedBranch = NormalizeBranch(branch);
            string key = $"{repo}@{normalizedBranch}";
            if (nodes.TryGetValue(key, out DependencyFlowNode existingNode))
            {
                return existingNode;
            }
            else
            {
                DependencyFlowNode newNode = new DependencyFlowNode(repo, normalizedBranch);
                nodes.Add(key, newNode);
                return newNode;
            }
        }
    }
}
