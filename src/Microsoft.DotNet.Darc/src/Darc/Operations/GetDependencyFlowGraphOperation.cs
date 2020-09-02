// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
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

        const int engLatestChannelId = 2;
        const int eng3ChannelId = 344;

        public override async Task<int> ExecuteAsync()
        {
            try
            {
                RemoteFactory remoteFactory = new RemoteFactory(_options);
                var barOnlyRemote = await remoteFactory.GetBarOnlyRemoteAsync(Logger);

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

                var flowGraph = await barOnlyRemote.GetDependencyFlowGraph(
                    targetChannel?.Id ?? 0,
                    _options.Days,
                    includeArcade: true,
                    includeBuildTimes: _options.IncludeBuildTimes,
                    includeDisabledSubscriptions: _options.IncludeDisabledSubscriptions,
                    includedFrequencies: _options.IncludedFrequencies?.ToList());

                await LogGraphVizAsync(targetChannel, flowGraph, _options.IncludeBuildTimes);

                return Constants.SuccessCode;
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine(e.Message);
                return Constants.ErrorCode;
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, "Something failed while getting the dependency graph.");
                return Constants.ErrorCode;
            }
        }

        /// <summary>
        ///     Get an edge style
        /// </summary>
        /// <param name="edge"></param>
        /// <returns></returns>
        private string GetEdgeStyle(DependencyFlowEdge edge)
        {
            string color = edge.OnLongestBuildPath ? "color=\"red:invis:red\"" : "";
            switch (edge.Subscription.Policy.UpdateFrequency)
            {
                case UpdateFrequency.EveryBuild:
                    // Solid
                    return $"{color} style=bold";
                case UpdateFrequency.EveryDay:
                case UpdateFrequency.TwiceDaily:
                case UpdateFrequency.EveryWeek:
                    return $"{color} style=dashed";
                case UpdateFrequency.None:
                    return $"{color} style=dotted";
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
        private async Task LogGraphVizAsync(Channel targetChannel, DependencyFlowGraph graph, bool includeBuildTimes)
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

                    string style = node.OnLongestBuildPath ? "style=\"diagonals,bold\" color=red" : "";

                    // First add the node name
                    nodeBuilder.Append($"    {UxHelpers.CalculateGraphVizNodeName(node)}");

                    // Then add the label.  label looks like [label="<info here>"]
                    nodeBuilder.Append($"[{style}\nlabel=\"");

                    // Append friendly repo name
                    nodeBuilder.Append(UxHelpers.GetSimpleRepoName(node.Repository));
                    nodeBuilder.Append(@"\n");

                    // Append branch name
                    nodeBuilder.Append(node.Branch);

                    if (includeBuildTimes)
                    {
                        // Append best case
                        nodeBuilder.Append(@"\n");
                        nodeBuilder.Append($"Best Case: {Math.Round(node.BestCasePathTime, 2, MidpointRounding.AwayFromZero)} min");
                        nodeBuilder.Append(@"\n");

                        // Append worst case
                        nodeBuilder.Append($"Worst Case: {Math.Round(node.WorstCasePathTime, 2, MidpointRounding.AwayFromZero)} min");
                        nodeBuilder.Append(@"\n");

                        // Append build times
                        nodeBuilder.Append($"Official Build Time: {Math.Round(node.OfficialBuildTime, 2, MidpointRounding.AwayFromZero)} min");
                        nodeBuilder.Append(@"\n");

                        nodeBuilder.Append($"PR Build Time: {Math.Round(node.PrBuildTime, 2, MidpointRounding.AwayFromZero)} min");
                    }

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
                await writer.WriteLineAsync("        g[style = \"diagonals,bold\" color=red];");
                await writer.WriteLineAsync("        h[style = \"diagonals,bold\" color=red];");
                await writer.WriteLineAsync("        c->d[label = \"Updated Every Build\", style = bold];");
                await writer.WriteLineAsync("        a->b[label = \"Updated Every Day\", style = dashed];");
                await writer.WriteLineAsync("        e->f[label = \"Disabled/Updated On-demand\", style = dotted];");
                await writer.WriteLineAsync("        g->h[label = \"Longest Build Path\", color=\"red:invis:red\"];");
                await writer.WriteLineAsync("    }");
                await writer.WriteLineAsync("    subgraph cluster2{");
                await writer.WriteLineAsync("        rankdir=BT;");
                await writer.WriteLineAsync("        style=invis;");
                await writer.WriteLineAsync("        note[shape=plaintext label=\"Best Case: Time through the graph assuming no dependency flow\nWorst Case: Time through the graph with dependency flow (PRs)\"];");
                await writer.WriteLineAsync("    }");
                await writer.WriteLineAsync("    d->note[lhead=cluster2, ltail=cluster1, style=invis];");
                
            
                await writer.WriteLineAsync("}");
            }
        }
    }
}
