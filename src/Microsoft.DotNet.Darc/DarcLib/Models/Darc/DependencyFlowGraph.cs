// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

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
                targetNode.IncomingEdges.Remove(outgoingEdge);
                RecalculateInputChannels(targetNode);
            }
        }
    }

    /// <summary>
    ///     Recalculate the input channels based on the input edges.
    /// </summary>
    /// <param name="node">Node to calculate the input edges for.</param>
    private static void RecalculateInputChannels(DependencyFlowNode node)
    {
        node.InputChannels = [.. node.IncomingEdges.Select(e => e.Subscription.Channel.Name)];
    }

    public void RemoveEdge(DependencyFlowEdge edge)
    {
        if (Edges.Remove(edge))
        {
            edge.From.OutgoingEdges.Remove(edge);
            DependencyFlowNode targetNode = edge.To;
            edge.To.IncomingEdges.Remove(edge);
            RecalculateInputChannels(targetNode);
        }
    }

    /// <summary>
    ///     Prune away uninteresting nodes and edges from the graph
    /// </summary>
    /// <param name="isInterestingNode">Returns true if the node is an interesting node</param>
    /// <param name="isInterestingEdge">Returns true if the edge is an interesting edge</param>
    /// <remarks>
    ///     Starting with the set of interesting nodes as indicated by <paramref name="isInterestingNode"/>,
    ///     remove all edges that are not interesting, and all nodes that are not reachable by an interesting
    ///     edge.
    /// </remarks>
    public void PruneGraph(Func<DependencyFlowNode, bool> isInterestingNode,
        Func<DependencyFlowEdge, bool> isInterestingEdge)
    {
        var unreachableNodes = new HashSet<DependencyFlowNode>(Nodes);
        var unreachableEdges = new HashSet<DependencyFlowEdge>(Edges);
        var nodes = new Stack<DependencyFlowNode>();

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

                if (!unreachableNodes.Remove(currentNode))
                {
                    // Nothing to do
                    continue;
                }
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

    /// <summary>
    ///    Mark the back edges of the graph so that they can be ignored when walking it.
    ///    Determine the backedges by computing dominators for each node. A node n is said to
    ///    dominate another node if every path from the start to the other node must go through
    ///    n.
    /// </summary>
    public void MarkBackEdges()
    {
        // In this, we will interpret the root of the graph as those nodes that have no outgoing edges
        // (call them 'products').  Connect all of these via a start node.
        // Add a start node to the graph (a node that connects all nodes that have no outgoing edges
        // We will also reverse the graph by flipping the interpretation of incoming and outgoing.
        var startNode = new DependencyFlowNode("start", "start", "start");
        foreach (DependencyFlowNode node in Nodes)
        {
            if (node.OutgoingEdges.Count == 0)
            {
                var newEdge = new DependencyFlowEdge(node, startNode, null);
                startNode.IncomingEdges.Add(newEdge);
                node.OutgoingEdges.Add(newEdge);
            }
        }
        Nodes.Add(startNode);

        // Dominator set for each node starts with the full set of nodes.
        Dictionary<DependencyFlowNode, HashSet<DependencyFlowNode>> dominators = [];
        foreach (DependencyFlowNode node in Nodes)
        {
            dominators.Add(node, [.. Nodes]);
        }

        var workList = new Queue<DependencyFlowNode>();
        workList.Enqueue(startNode);

        while (workList.Count != 0)
        {
            DependencyFlowNode currentNode = workList.Dequeue();

            // Compute a new dominator set for this node. Remember we are 
            // flipping the interepretation of incoming and outgoing
            HashSet<DependencyFlowNode> newDom = null;

            IEnumerable<DependencyFlowNode> predecessors = currentNode.OutgoingEdges.Select(e => e.To);
            IEnumerable<DependencyFlowNode> successors = currentNode.IncomingEdges.Select(e => e.From);

            // Compute the intersection of the dominators for the predecessors
            foreach (DependencyFlowNode predNode in predecessors)
            {
                if (newDom == null)
                {
                    newDom = [.. dominators[predNode]];
                }
                else
                {
                    newDom.IntersectWith(dominators[predNode]);
                }
            }

            // Add the current node
            newDom ??= [];
            newDom.Add(currentNode);

            // Compare to the existing dominator set for this node
            if (!dominators[currentNode].SetEquals(newDom))
            {
                dominators[currentNode] = newDom;

                // Queue all of the successors
                foreach (DependencyFlowNode succ in successors)
                {
                    workList.Enqueue(succ);
                }
            }
        }

        // Determine backedges
        List<DependencyFlowEdge> toRemove = [];
        foreach (DependencyFlowEdge edge in Edges)
        {
            if (dominators[edge.To].Contains(edge.From))
            {
                edge.BackEdge = true;
            }
        }

        // Remove the start node we created and links to it
        foreach (DependencyFlowEdge edge in startNode.IncomingEdges)
        {
            RemoveEdge(edge);
        }

        RemoveNode(startNode);
    }

    /// <summary>
    ///     Calculate the longest build path time from each node in the flow graph
    /// </summary>
    /// <remarks>
    ///     Starting with the set of root node (nodes with no outgoing edges), compute the
    ///     longest build path time using a breadth first search
    /// </remarks>
    public void CalculateLongestBuildPaths()
    {
        var roots = Nodes.Where(n => n.OutgoingEdges.Count == 0).ToList();
        Dictionary<DependencyFlowNode, HashSet<DependencyFlowNode>> visitedNodes = [];

        var nodesToVisit = new Queue<DependencyFlowNode>();

        foreach (DependencyFlowNode root in roots)
        {
            nodesToVisit.Enqueue(root);

            while (nodesToVisit.Count > 0)
            {
                DependencyFlowNode node = nodesToVisit.Dequeue();
                if (visitedNodes.TryGetValue(node, out HashSet<DependencyFlowNode> value))
                {
                    value.Add(node);
                }
                else
                {
                    visitedNodes.Add(node, [node]);
                }

                foreach (DependencyFlowEdge edge in node.IncomingEdges)
                {
                    DependencyFlowNode child = edge.From;

                    if (!visitedNodes[node].Contains(child) && !nodesToVisit.Contains(child))
                    {
                        if (visitedNodes.TryGetValue(child, out HashSet<DependencyFlowNode> value1))
                        {
                            value1.UnionWith(visitedNodes[node]);
                        }
                        else
                        {
                            visitedNodes.Add(child, [.. visitedNodes[node]]);
                        }

                        nodesToVisit.Enqueue(child);
                    }
                }

                node.CalculateLongestPathTime();
            }
        }
    }

    /// <summary>
    ///     Determine and mark the absolute longest build path in the flow graph, based on the Best Case time.
    /// </summary>
    public void MarkLongestBuildPath()
    {
        // Find the node with the worst best case time, we will treat it as the starting point and walk down the path
        // from this node to a product node
        DependencyFlowNode startNode = Nodes
            .Where(n => !n.IsToolingOnly)
            .OrderByDescending(n => n.BestCasePathTime)
            .FirstOrDefault();

        if (startNode != null)
        {
            startNode.OnLongestBuildPath = true;
            MarkLongestPath(startNode);
        }
    }

    private static void MarkLongestPath(DependencyFlowNode node)
    {
        // The edges we are interested in are those that haven't been marked as on the longest build path 
        // and aren't back edges, both of which indicate a cycle
        var edgesOfInterest = node.OutgoingEdges
            .Where(e => !e.OnLongestBuildPath && !e.BackEdge && !e.IsToolingOnly)
            .ToList();

        if (edgesOfInterest.Count > 0)
        {
            DependencyFlowEdge pathEdge = edgesOfInterest.Aggregate((e1, e2) => e1.To.BestCasePathTime > e2.To.BestCasePathTime ? e1 : e2);

            // Mark the edge and the node as on the longest build path
            pathEdge.OnLongestBuildPath = true;
            pathEdge.To.OnLongestBuildPath = true;
            MarkLongestPath(pathEdge.To);
        }
    }

    public static async Task<DependencyFlowGraph> BuildAsync(
        List<DefaultChannel> defaultChannels,
        List<Subscription> subscriptions,
        IBasicBarClient barClient,
        int days)
    {
        // Dictionary of nodes. Key is the repo+branch
        var nodes = new Dictionary<string, DependencyFlowNode>(
            StringComparer.OrdinalIgnoreCase);
        List<DependencyFlowEdge> edges = [];

        // First create all the channel nodes. There may be disconnected
        // nodes in the graph, so we must process all channels and all subscriptions
        foreach (DefaultChannel channel in defaultChannels)
        {
            DependencyFlowNode flowNode = GetOrCreateNode(channel.Repository, channel.Branch, nodes);

            // Add the build times
            if (channel.Id != default)
            {
                BuildTime buildTime = await barClient.GetBuildTimeAsync(channel.Id, days);
                flowNode.OfficialBuildTime = buildTime.OfficialBuildTime ?? 0;
                flowNode.PrBuildTime = buildTime.PrBuildTime ?? 0;
                flowNode.GoalTimeInMinutes = buildTime.GoalTimeInMinutes ?? 0;
            }
            else
            {
                flowNode.OfficialBuildTime = 0;
                flowNode.PrBuildTime = 0;
            }

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
            // Find all input nodes by looking up the default channels of the subscription input channel and repository.
            // This may return no nodes if there is no default channel for the inputs.
            IEnumerable<DefaultChannel> inputDefaultChannels = defaultChannels.Where(d => d.Channel.Name == subscription.Channel.Name &&
                d.Repository.Equals(subscription.SourceRepository, StringComparison.OrdinalIgnoreCase));
            foreach (DefaultChannel defaultChannel in inputDefaultChannels)
            {
                DependencyFlowNode sourceNode = GetOrCreateNode(defaultChannel.Repository, defaultChannel.Branch, nodes);

                var newEdge = new DependencyFlowEdge(sourceNode, destinationNode, subscription);
                destinationNode.IncomingEdges.Add(newEdge);
                sourceNode.OutgoingEdges.Add(newEdge);
                edges.Add(newEdge);
            }
        }

        return new DependencyFlowGraph([.. nodes.Select(kv => kv.Value)], edges);
    }

    private static DependencyFlowNode GetOrCreateNode(
        string repo,
        string branch,
        Dictionary<string, DependencyFlowNode> nodes)
    {
        var key = $"{repo}@{branch}";
        if (nodes.TryGetValue(key, out DependencyFlowNode existingNode))
        {
            return existingNode;
        }
        else
        {
            var newNode = new DependencyFlowNode(repo, branch, Guid.NewGuid().ToString());
            nodes.Add(key, newNode);
            return newNode;
        }
    }

    /// <summary>
    ///     If pruning the graph is desired, determine whether a node is interesting.
    /// </summary>
    /// <param name="node">Node</param>
    /// <returns>True if the node is interesting, false otherwise</returns>
    public static bool IsInterestingNode(string targetChannel, DependencyFlowNode node)
    {
        return node.OutputChannels.Any(c => c == targetChannel);
    }

    /// <summary>
    ///     If pruning the graph is desired, determine whether an edge is interesting
    /// </summary>
    /// <param name="edge">Edge</param>
    /// <returns>True if the edge is interesting, false otherwise.</returns>
    public static bool IsInterestingEdge(DependencyFlowEdge edge, bool includeDisabledSubscriptions, IEnumerable<string> includedFrequencies)
    {
        if (!includeDisabledSubscriptions && !edge.Subscription.Enabled)
        {
            return false;
        }
        if (!includedFrequencies.Any(s => s.Equals(edge.Subscription.Policy.UpdateFrequency.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return true;
    }
}
