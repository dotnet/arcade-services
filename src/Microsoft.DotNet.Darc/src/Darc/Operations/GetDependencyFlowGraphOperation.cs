// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Invitation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class GetDependencyFlowGraphOperation : Operation
    {
        private GetDependencyFlowGraphCommandLineOptions _options;

        public GetDependencyFlowGraphOperation(GetDependencyFlowGraphCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            try
            {
                RemoteFactory remoteFactory = new RemoteFactory(_options);
                var barOnlyRemote = await remoteFactory.GetBarOnlyRemoteAsync(Logger);

                List<DefaultChannel> defaultChannels = (await barOnlyRemote.GetDefaultChannelsAsync()).ToList();
                defaultChannels.Add(
                    new DefaultChannel(0, "https://github.com/dotnet/arcade", true)
                    {
                        Branch = "refs/heads/master",
                        Channel = await barOnlyRemote.GetChannelAsync(".NET Tools - Latest")
                    }
                );
                defaultChannels.Add(
                    new DefaultChannel(0, "https://github.com/dotnet/arcade", true)
                    {
                        Branch = "refs/heads/release/3.x",
                        Channel = await barOnlyRemote.GetChannelAsync(".NET 3 Tools")
                    }
                );
                List<Subscription> subscriptions = (await barOnlyRemote.GetSubscriptionsAsync()).ToList();

                // Build, then prune out what we don't want to see if the user specified
                // channels.
                DependencyFlowGraph flowGraph = DependencyFlowGraph.Build(defaultChannels, subscriptions);

                Channel targetChannel = null;
                if (!string.IsNullOrEmpty(_options.Channel))
                {
                    // Resolve the channel.
                    targetChannel = await UxHelpers.ResolveSingleChannel(barOnlyRemote, _options.Channel);
                    if (targetChannel == null)
                    {
                        return Constants.ErrorCode;
                    }
                }

                if (targetChannel != null)
                {
                    flowGraph.PruneGraph(node => IsInterestingNode(targetChannel, node), edge => IsInterestingEdge(edge));
                }

                // For each node, determine number of changes that are made for a change starting at that node.
                // That is, the number of times each every other node is visited by a walk starting there.
                foreach (var node in flowGraph.Nodes)
                {
                    Dictionary<DependencyFlowNode, int> visitCounts = new Dictionary<DependencyFlowNode, int>();
                    Stack<DependencyFlowNode> visitStack = new Stack<DependencyFlowNode>();
                    visitStack.Push(node);
                    while (visitStack.Count != 0)
                    {
                        var currentNode = visitStack.Pop();
                        if (visitCounts.ContainsKey(currentNode))
                        {
                            visitCounts[currentNode]++;
                        }
                        else
                        {
                            visitCounts.Add(currentNode, 1);
                        }
                        foreach (var edge in currentNode.OutgoingEdges)
                        {
                            visitStack.Push(edge.To);
                        }
                    }

                    Console.WriteLine($"{node.Repository}:");
                    int total = 0;
                    foreach (var visitCount in visitCounts)
                    {
                        Console.WriteLine($"  {visitCount.Key.Repository} = {visitCount.Value}");
                        total += visitCount.Value;
                    }
                    Console.WriteLine($"  Total = {total}");
                }

                await LogGraphViz(targetChannel, flowGraph);

                return Constants.SuccessCode;
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, "Something failed while getting the dependency graph.");
                return Constants.ErrorCode;
            }
        }

        /// <summary>
        ///     If pruning the graph is desired, determine whether a node is interesting.
        /// </summary>
        /// <param name="node">Node</param>
        /// <returns>True if the node is interesting, false otherwise</returns>
        private bool IsInterestingNode(Channel targetChannel, DependencyFlowNode node)
        {
            return node.OutputChannels.Any(c => c == targetChannel.Name);
        }

        /// <summary>
        ///     If pruning the graph is desired, determine whether an edge is interesting
        /// </summary>
        /// <param name="edge">Edge</param>
        /// <returns>True if the edge is interesting, false otherwise.</returns>
        private bool IsInterestingEdge(DependencyFlowEdge edge)
        {
            if (!_options.IncludeDisabledSubscriptions && !edge.Subscription.Enabled)
            {
                return false;
            }
            if (!_options.IncludedFrequencies.Any(s => s.Equals(edge.Subscription.Policy.UpdateFrequency.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        ///     Get an edge style
        /// </summary>
        /// <param name="edge"></param>
        /// <returns></returns>
        private string GetEdgeStyle(DependencyFlowEdge edge)
        {
            switch (edge.Subscription.Policy.UpdateFrequency)
            {
                case UpdateFrequency.EveryBuild:
                    // Solid
                    return "style=bold";
                case UpdateFrequency.EveryDay:
                case UpdateFrequency.TwiceDaily:
                case UpdateFrequency.EveryWeek:
                    return "style=dashed";
                case UpdateFrequency.None:
                    return "style=dotted";
                default:
                    throw new NotImplementedException("Unknown update frequency");
            }
        }

        /// <summary>
        ///     Log the graph in graphviz (dot) format.
        /// </summary>
        /// <param name="graph">Graph to log</param>
        /// <remarks>
        /// Example of a graphviz graph description
        ///  
        /// digraph graphname {
        ///    a -> b -> c;
        ///    b -> d;
        /// }
        ///  
        /// For more info see https://www.graphviz.org/
        /// </remarks>
        /// <returns>Async task</returns>
        private async Task LogGraphViz(Channel targetChannel, DependencyFlowGraph graph)
        {
            StringBuilder subgraphClusterWriter = null;
            bool writeToSubgraphCluster = targetChannel != null;
            if (writeToSubgraphCluster)
            {
                subgraphClusterWriter = new StringBuilder();
                subgraphClusterWriter.AppendLine($"    subgraph cluster_{UxHelpers.CalculateGraphVizNodeName(targetChannel.Name)} {{");
                subgraphClusterWriter.AppendLine($"        label = \"{targetChannel.Name}\"");
            }

            using (StreamWriter writer = UxHelpers.GetOutputFileStreamOrConsole(_options.GraphVizOutputFile))
            {
                await writer.WriteLineAsync("digraph repositoryGraph {");
                await writer.WriteLineAsync("    node [shape=record]");
                foreach (DependencyFlowNode node in graph.Nodes)
                {
                    StringBuilder nodeBuilder = new StringBuilder();

                    // First add the node name
                    nodeBuilder.Append($"    {UxHelpers.CalculateGraphVizNodeName(node)}");

                    // Then add the label.  label looks like [label="<info here>"]
                    nodeBuilder.Append("[label=\"");

                    // Append friendly repo name
                    nodeBuilder.Append(UxHelpers.GetSimpleRepoName(node.Repository));
                    nodeBuilder.Append(@"\n");

                    // Append branch name
                    nodeBuilder.Append(node.Branch);

                    // Append end of label and end of node.
                    nodeBuilder.Append("\"];");

                    // If highlighting a specific channel, Add those nodes to a subgraph cluster
                    // if they output to the subgraph cluster.
                    if (writeToSubgraphCluster && node.OutputChannels.Any(c => c == targetChannel.Name))
                    {
                        subgraphClusterWriter.AppendLine($"        {UxHelpers.CalculateGraphVizNodeName(node)}");
                    }

                    // Write it out.
                    await writer.WriteLineAsync(nodeBuilder.ToString());
                }

                // Now write all the edges
                foreach (DependencyFlowEdge edge in graph.Edges)
                {
                    string fromNode = UxHelpers.CalculateGraphVizNodeName(edge.From);
                    string toNode = UxHelpers.CalculateGraphVizNodeName(edge.To);
                    string label = $"{edge.Subscription.Channel.Name} ({edge.Subscription.Policy.UpdateFrequency})";

                    if (writeToSubgraphCluster && edge.Subscription.Channel.Name == targetChannel.Name)
                    {
                        subgraphClusterWriter.AppendLine($"    {fromNode} -> {toNode} [{GetEdgeStyle(edge)}]");
                    }
                    else
                    {
                        await writer.WriteLineAsync($"    {fromNode} -> {toNode} [{GetEdgeStyle(edge)}]");
                    }
                }

                if (writeToSubgraphCluster)
                {
                    await writer.WriteLineAsync(subgraphClusterWriter.ToString());
                    await writer.WriteLineAsync("    }");
                }

                // Write a legend

                await writer.WriteLineAsync("    subgraph cluster1 {");
                await writer.WriteLineAsync("        rankdir=RL;");
                await writer.WriteLineAsync("        label = \"Legend\"");
                await writer.WriteLineAsync("        shape = rectangle;");
                await writer.WriteLineAsync("        color = black;");
                await writer.WriteLineAsync("        a[style = invis];");
                await writer.WriteLineAsync("        b[style = invis];");
                await writer.WriteLineAsync("        c[style = invis];");
                await writer.WriteLineAsync("        d[style = invis];");
                await writer.WriteLineAsync("        e[style = invis];");
                await writer.WriteLineAsync("        f[style = invis];");
                await writer.WriteLineAsync("        c->d[label = \"Updated Every Build\", style = bold];");
                await writer.WriteLineAsync("        a->b[label = \"Updated Every Day\", style = dashed];");
                await writer.WriteLineAsync("        e->f[label = \"Disabled/Updated On-demand\", style = dotted];");
                await writer.WriteLineAsync("    }");
            
                await writer.WriteLineAsync("}");
            }
        }
    }
}
