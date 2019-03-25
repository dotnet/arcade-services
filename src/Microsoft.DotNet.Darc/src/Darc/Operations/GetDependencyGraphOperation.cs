// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class GetDependencyGraphOperation : Operation
    {
        private GetDependencyGraphCommandLineOptions _options;
        private readonly HashSet<string> _flatList = new HashSet<string>();

        public GetDependencyGraphOperation(GetDependencyGraphCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IEnumerable<DependencyDetail> rootDependencies = null;
                DependencyGraph graph;
                RemoteFactory remoteFactory = new RemoteFactory(_options);

                if (!_options.Local)
                {
                    NodeDiff diffOption = NodeDiff.None;
                    // Check node diff options
                    switch(_options.DeltaFrom.ToLowerInvariant())
                    {
                        case "none":
                            break;
                        case "newest-in-channel":
                            diffOption = NodeDiff.LatestInChannel;
                            break;
                        case "newest-in-graph":
                            diffOption = NodeDiff.LatestInGraph;
                            break;
                        default:
                            Console.WriteLine("Unknown --delta-from option, please see help.");
                            return Constants.ErrorCode;
                    }

                    // If the repo uri and version are set, then call the graph
                    // build operation based on those.  Both should be set in this case.
                    // If they are not set, then gather the initial set based on the local repository,
                    // and then call the graph build with that root set.

                    if (!string.IsNullOrEmpty(_options.RepoUri))
                    {
                        if (string.IsNullOrEmpty(_options.Version))
                        {
                            Console.WriteLine("If --repo is set, --version should be supplied");
                            return Constants.ErrorCode;
                        }

                        Console.WriteLine($"Getting root dependencies from {_options.RepoUri}@{_options.Version}...");

                        // Grab root dependency set. The graph build can do this, but
                        // if an original asset name is passed, then this will do the initial filtering.
                        IRemote rootRepoRemote = await  remoteFactory.GetRemoteAsync(_options.RepoUri, Logger);
                        rootDependencies = await rootRepoRemote.GetDependenciesAsync(
                            _options.RepoUri,
                            _options.Version,
                            _options.AssetName);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(_options.Version))
                        {
                            Console.WriteLine("If --version is supplied, then --repo is required");
                            return Constants.ErrorCode;
                        }

                        Console.WriteLine($"Getting root dependencies from local repository...");

                        // Grab root dependency set from local repo
                        Local local = new Local(Logger);
                        rootDependencies = await local.GetDependenciesAsync(
                            _options.AssetName);
                    }

                    Console.WriteLine($"Building repository dependency graph...");

                    rootDependencies = FilterToolsetDependencies(rootDependencies);

                    if (!rootDependencies.Any())
                    {
                        Console.WriteLine($"No root dependencies found, exiting.");
                        return Constants.ErrorCode;
                    }

                    DependencyGraphBuildOptions graphBuildOptions = new DependencyGraphBuildOptions()
                    {
                        IncludeToolset = _options.IncludeToolset,
                        LookupBuilds = diffOption != NodeDiff.None || !_options.SkipBuildLookup,
                        NodeDiff = diffOption
                    };

                    // Build graph
                    graph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
                        remoteFactory,
                        rootDependencies,
                        _options.RepoUri ?? LocalHelpers.GetRootDir(Logger),
                        _options.Version ?? LocalHelpers.GetGitCommit(Logger),
                        graphBuildOptions,
                        Logger);
                }
                else
                {
                    Console.WriteLine($"Getting root dependencies from local repository...");

                    Local local = new Local(Logger);
                    rootDependencies = await local.GetDependenciesAsync(
                        _options.AssetName);

                    rootDependencies = FilterToolsetDependencies(rootDependencies);

                    if (!rootDependencies.Any())
                    {
                        Console.WriteLine($"No root dependencies found, exiting.");
                        return Constants.ErrorCode;
                    }

                    Console.WriteLine($"Building repository dependency graph from local information...");

                    DependencyGraphBuildOptions graphBuildOptions = new DependencyGraphBuildOptions()
                    {
                        IncludeToolset = _options.IncludeToolset,
                        LookupBuilds = false,
                        NodeDiff = NodeDiff.None
                    };

                    // Build graph using only local resources
                    graph = await DependencyGraph.BuildLocalDependencyGraphAsync(
                        rootDependencies,
                        graphBuildOptions,
                        Logger,
                        LocalHelpers.GetRootDir(Logger),
                        LocalHelpers.GetGitCommit(Logger),
                        _options.ReposFolder,
                        _options.RemotesMap);
                }

                if (_options.Flat)
                {
                    await LogFlatDependencyGraph(graph);
                }
                else
                {
                    await LogDependencyGraph(graph);
                }

                if (!string.IsNullOrEmpty(_options.GraphVizOutputFile))
                {
                    await LogGraphViz(graph);
                }

                return Constants.SuccessCode;
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, "Something failed while getting the dependency graph.");
                return Constants.ErrorCode;
            }
        }

        private IEnumerable<DependencyDetail> FilterToolsetDependencies(IEnumerable<DependencyDetail> dependencies)
        {
            if (!_options.IncludeToolset)
            {
                Console.WriteLine($"Removing toolset dependencies...");
                return dependencies.Where(dependency => dependency.Type != DependencyType.Toolset);
            }
            return dependencies;
        }

        /// <summary>
        ///     Log basic node details that are common to flat/normal and coherency
        ///     path views
        /// </summary>
        /// <param name="node">Node</param>
        /// <param name="indent">Indentation</param>
        /// <example>
        ///       - Repo:     https://github.com/dotnet/wpf
        ///         Commit:   99112590688a44837276e20e9c91ef41fd54c64b
        ///         Delta:    latest
        ///         Builds:
        ///         - 20190228.4 (2/28/2019 12:57 PM)
        /// </example>
        private async Task LogBasicNodeDetails(StreamWriter writer, DependencyGraphNode node, string indent)
        {
            await writer.WriteLineAsync($"{indent}- Repo:     {node.RepoUri}");
            await writer.WriteLineAsync($"{indent}  Commit:   {node.Commit}");

            StringBuilder deltaString = new StringBuilder($"{indent}  Delta:    ");
            GitDiff diffFrom = node.DiffFrom;

            // Log the delta. Depending on user options, deltas from latest build,
            // other builds in the graph, etc. may have been calculated as part of the
            // graph build.  The delta is a diff in a node commit and another commit.
            // For the purposes of the user display for the dependency graph, we really
            // only care about the ahead/behind information.

            if (diffFrom != null)
            {
                if (diffFrom.Valid)
                {
                    if (diffFrom.Ahead != 0 || diffFrom.Behind != 0)
                    {
                        if (diffFrom.Ahead != 0)
                        {
                            deltaString.Append($"ahead {diffFrom.Ahead}");
                        }
                        if (diffFrom.Behind != 0)
                        {
                            if (diffFrom.Ahead != 0)
                            {
                                deltaString.Append(", ");
                            }
                            deltaString.Append($"behind {diffFrom.Behind}");
                        }
                        deltaString.Append($" commits vs. {diffFrom.BaseVersion}");
                    }
                    else
                    {
                        deltaString.Append("latest");
                    }
                }
                else
                {
                    deltaString.Append("unknown");
                }
                await writer.WriteLineAsync(deltaString.ToString());
            }
            if (node.ContributingBuilds != null)
            {
                if (node.ContributingBuilds.Any())
                {
                    await writer.WriteLineAsync($"{indent}  Builds:");
                    foreach (var build in node.ContributingBuilds)
                    {
                        await writer.WriteLineAsync($"{indent}  - {build.AzureDevOpsBuildNumber} ({build.DateProduced.ToLocalTime().ToString("g")})");
                    }
                }
                else
                {
                    await writer.WriteLineAsync($"{indent}  Builds: []");
                }
            }
        }

        /// <summary>
        ///     Retrieve either a new StreamWriter for the specified output file,
        ///     or if the output file name is empty, create a new StreamWriter
        ///     wrapping standard out.
        /// </summary>
        /// <param name="outputFile">Output file name.</param>
        /// <returns>New stream writer</returns>
        /// <remarks>
        ///     The StreamWriter can be disposed of even if it's wrapping Console.Out.
        ///     The underlying stream is only disposed of if the stream writer owns it.
        /// </remarks>
        private StreamWriter GetOutputFileStreamOrConsole(string outputFile)
        {
            StreamWriter outputStream = null;
            if (!string.IsNullOrEmpty(outputFile))
            {
                string fullPath = Path.GetFullPath(outputFile);
                string directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                outputStream = new StreamWriter(fullPath);
            }
            else
            {
                outputStream = new StreamWriter(Console.OpenStandardOutput());
            }
            return outputStream;
        }

        /// <summary>
        ///     Log the dependency graph as a simple flat list of repo/sha combinations
        ///     that contribute to this graph.
        /// </summary>
        /// <param name="graph">Graph to log</param>
        private async Task LogFlatDependencyGraph(DependencyGraph graph)
        {
            using (StreamWriter writer = GetOutputFileStreamOrConsole(_options.OutputFile))
            {
                await writer.WriteLineAsync($"Repositories:");
                foreach (DependencyGraphNode node in graph.Nodes)
                {
                    await LogBasicNodeDetails(writer, node, "  ");
                }
                await LogIncoherencies(writer, graph);
            }
        }

        private async Task LogDependencyGraph(DependencyGraph graph)
        {
            using (StreamWriter writer = GetOutputFileStreamOrConsole(_options.OutputFile))
            {
                await writer.WriteLineAsync($"Repositories:");
                await LogDependencyGraphNode(writer, graph.Root, "  ");
                await LogIncoherencies(writer, graph);
            }
        }

        private string GetSimpleRepoName(string repoUri)
        {
            int lastSlash = repoUri.LastIndexOf("/");
            if ((lastSlash != -1) && (lastSlash < (repoUri.Length - 1)))
            {
                return repoUri.Substring(lastSlash + 1);
            }
            return repoUri;
        }

        private string CalculateGraphVizNodeName(DependencyGraphNode node)
        {
            return GetSimpleRepoName(node.RepoUri)
                .Replace(".", "")
                .Replace("-", "") + node.Commit;
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
        private async Task LogGraphViz(DependencyGraph graph)
        {
            using (StreamWriter writer = GetOutputFileStreamOrConsole(_options.GraphVizOutputFile))
            {
                await writer.WriteLineAsync("digraph repositoryGraph {");
                await writer.WriteLineAsync("    node [shape=record]");
                foreach (DependencyGraphNode node in graph.Nodes)
                {
                    StringBuilder nodeBuilder = new StringBuilder();
                    
                    // First add the node name
                    nodeBuilder.Append($"    {CalculateGraphVizNodeName(node)}");
                    
                    // Then add the label.  label looks like [label="<info here>"]
                    nodeBuilder.Append("[label=\"");
                    
                    // Append friendly repo name
                    nodeBuilder.Append(GetSimpleRepoName(node.RepoUri));
                    nodeBuilder.Append(@"\n");
                    
                    // Append short commit sha
                    nodeBuilder.Append(node.Commit.Substring(0, node.Commit.Length < 10 ? node.Commit.Length : 10));
                    
                    // Append a build string (with newline) if available
                    if (node.ContributingBuilds != null && node.ContributingBuilds.Any())
                    {
                        Build newestBuild = node.ContributingBuilds.OrderByDescending(b => b.DateProduced).First();
                        nodeBuilder.Append($"\\n{newestBuild.DateProduced.ToString("g")} (UTC)");
                    }

                    // Append a diff string if the graph contains diff info.
                    GitDiff diffFrom = node.DiffFrom;
                    if (diffFrom != null)
                    {
                        if (!diffFrom.Valid)
                        {
                            nodeBuilder.Append("\\ndiff unknown");
                        }
                        else if (diffFrom.Ahead != 0 || diffFrom.Behind != 0)
                        {
                            if (node.DiffFrom.Ahead != 0)
                            {
                                nodeBuilder.Append($"\\nahead: {node.DiffFrom.Ahead} commits");
                            }
                            if (node.DiffFrom.Behind != 0)
                            {
                                nodeBuilder.Append($"\\nbehind: {node.DiffFrom.Behind} commits");
                            }
                        }
                        else
                        {
                            nodeBuilder.Append("\\nlatest");
                        }
                    }

                    // Append end of label and end of node.
                    nodeBuilder.Append("\"];");

                    // Write it out.
                    await writer.WriteLineAsync(nodeBuilder.ToString());

                    // Now write the edges
                    foreach (DependencyGraphNode childNode in node.Children)
                    {
                        await writer.WriteLineAsync($"    {CalculateGraphVizNodeName(node)} -> {CalculateGraphVizNodeName(childNode)}");
                    }
                }

                await writer.WriteLineAsync("}");
            }
        }

        /// <summary>
        ///     Log incoherencies in the graph, places where repos appear at different shas,
        ///     or dependencies appear at different version numbers.
        /// </summary>
        /// <param name="graph">Graph to log incoherencies for</param>
        private async Task LogIncoherencies(StreamWriter writer, DependencyGraph graph)
        {
            if (!graph.IncoherentNodes.Any() || !_options.IncludeCoherency)
            {
                return;
            }
            await writer.WriteLineAsync("Incoherent Dependencies:");
            foreach (DependencyDetail incoherentDependency in graph.IncoherentDependencies)
            {
                await writer.WriteLineAsync($"  - Repo:    {incoherentDependency.RepoUri}");
                await writer.WriteLineAsync($"    Commit:  {incoherentDependency.Commit}");
                await writer.WriteLineAsync($"    Name:    {incoherentDependency.Name}");
                await writer.WriteLineAsync($"    Version: {incoherentDependency.Version}");
                if (_options.IncludeToolset)
                {
                    await writer.WriteLineAsync($"    Type:    {incoherentDependency.Type}");
                }
            }

            await writer.WriteLineAsync("Incoherent Repositories:");
            foreach (DependencyGraphNode incoherentRoot in graph.IncoherentNodes)
            {
                await LogIncoherentPath(writer, incoherentRoot, null, "  ");
            }
        }

        private async Task LogIncoherentPath(StreamWriter writer, DependencyGraphNode currentNode, DependencyGraphNode childNode, string indent)
        {
            await LogBasicNodeDetails(writer, currentNode, indent);
            foreach (DependencyGraphNode parentNode in currentNode.Parents)
            {
                await LogIncoherentPath(writer, parentNode, currentNode, indent + "  ");
            }
        }

        /// <summary>
        ///     Log an individual dependency graph node.
        /// </summary>
        /// <param name="node">Node to log</param>
        /// <param name="indent">Current indentation level.</param>
        private async Task LogDependencyGraphNode(StreamWriter writer, DependencyGraphNode node, string indent)
        {
            // Log the repository information.
            await LogBasicNodeDetails(writer, node, indent);
            if (node.Dependencies != null && node.Dependencies.Any())
            {
                await writer.WriteLineAsync($"{indent}  Dependencies:");

                // Log the dependencies at this repository.
                foreach (DependencyDetail dependency in node.Dependencies)
                {
                    await writer.WriteLineAsync($"{indent}  - Name:    {dependency.Name}");
                    await writer.WriteLineAsync($"{indent}    Version: {dependency.Version}");
                    if (_options.IncludeToolset)
                    {
                        await writer.WriteLineAsync($"{indent}    Type:    {dependency.Type}");
                    }
                }
            }

            if (node.Children.Any())
            {
                await writer.WriteLineAsync($"{indent}  Input Repositories:");

                // Walk children
                foreach (DependencyGraphNode childNode in node.Children)
                {
                    await LogDependencyGraphNode(writer, childNode, $"{indent}  ");
                }
            }
        }
    }
}
