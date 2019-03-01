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
                        IRemote rootRepoRemote = remoteFactory.GetRemote(_options.RepoUri, Logger);
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
                        Local local = new Local(LocalHelpers.GetGitDir(Logger), Logger);
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
                        _options.RepoUri ?? LocalHelpers.GetGitDir(Logger),
                        _options.Version ?? LocalHelpers.GetGitCommit(Logger),
                        graphBuildOptions,
                        Logger);
                }
                else
                {
                    Console.WriteLine($"Getting root dependencies from local repository...");

                    Local local = new Local(LocalHelpers.GetGitDir(Logger), Logger);
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
                        LocalHelpers.GetGitDir(Logger),
                        LocalHelpers.GetGitCommit(Logger),
                        _options.ReposFolder,
                        _options.RemotesMap);
                }

                if (_options.Flat)
                {
                    LogFlatDependencyGraph(graph);
                }
                else
                {
                    LogDependencyGraph(graph);
                }

                if (!string.IsNullOrEmpty(_options.GraphVizOutputFile))
                {
                    LogGraphViz(graph);
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

        private void LogBasicNodeDetails(DependencyGraphNode node, string indent)
        {
            Console.WriteLine($"{indent}- Repo:     {node.RepoUri}");
            Console.WriteLine($"{indent}  Commit:   {node.Commit}");
            GitDiff diffFrom = node.DiffFrom;
            // Log the node diff if it was desired.
            if (diffFrom != null)
            {
                string deltaString = "unknown";
                if (diffFrom.Valid)
                {
                    if (diffFrom.Ahead != 0 || diffFrom.Behind != 0)
                    {
                        deltaString = "";
                        if (diffFrom.Ahead != 0)
                        {
                            deltaString = $"ahead {diffFrom.Ahead}";
                        }
                        if (diffFrom.Behind != 0)
                        {
                            if (deltaString != "")
                            {
                                deltaString += ", ";
                            }
                            deltaString += $"behind {diffFrom.Behind}";
                        }
                        deltaString += $" commits vs. {diffFrom.BaseVersion}";
                    }
                    else
                    {
                        deltaString = "latest";
                    }
                }
                Console.WriteLine($"{indent}  Delta:    {deltaString}");
            }
            if (node.ContributingBuilds != null)
            {
                if (node.ContributingBuilds.Any())
                {
                    Console.WriteLine($"{indent}  Builds:");
                    foreach (var build in node.ContributingBuilds)
                    {
                        Console.WriteLine($"{indent}  - {build.AzureDevOpsBuildNumber} ({build.DateProduced.Value.ToLocalTime().ToString("g")})");
                    }
                }
                else
                {
                    Console.WriteLine($"{indent}  Builds: []");
                }
            }
        }

        /// <summary>
        ///     Log the dependency graph as a simple flat list of repo/sha combinations
        ///     that contribute to this graph.
        /// </summary>
        /// <param name="graph">Graph to log</param>
        private void LogFlatDependencyGraph(DependencyGraph graph)
        {
            Console.WriteLine($"Repositories:");
            foreach (DependencyGraphNode node in graph.Nodes)
            {
                LogBasicNodeDetails(node, "  ");
            }
            LogIncoherencies(graph);
        }

        private void LogDependencyGraph(DependencyGraph graph)
        {
            Console.WriteLine($"Repositories:");
            LogDependencyGraphNode(graph.Root, "  ");
            LogIncoherencies(graph);
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

        private string GetGraphVizNodeName(DependencyGraphNode node)
        {
            return GetSimpleRepoName(node.RepoUri).Replace("-", "") + node.Commit;
        }

        private async void LogGraphViz(DependencyGraph graph)
        {
            string directory = Path.GetDirectoryName(_options.GraphVizOutputFile);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (StreamWriter writer = new StreamWriter(_options.GraphVizOutputFile))
            {
                await writer.WriteLineAsync("digraph repositoryGraph {");
                await writer.WriteLineAsync("    node [shape=record]");
                foreach (DependencyGraphNode node in graph.Nodes)
                {
                    string buildString = "";
                    if (node.ContributingBuilds != null && node.ContributingBuilds.Any())
                    {
                        Build newestBuild = node.ContributingBuilds.OrderByDescending(b => b.DateProduced).First();
                        buildString = $"\\n{newestBuild.DateProduced.Value.ToLocalTime().ToString("g")}";
                    }

                    string diffString = "";
                    GitDiff diffFrom = node.DiffFrom;
                    if (diffFrom != null)
                    {
                        if (!diffFrom.Valid)
                        {
                            diffString = "\\ndiff unknown";
                        }
                        else if (diffFrom.Ahead != 0 || diffFrom.Behind != 0)
                        {
                            if (node.DiffFrom.Ahead != 0)
                            {
                                diffString += $"\\nahead: {node.DiffFrom.Ahead} commits";
                            }
                            if (node.DiffFrom.Behind != 0)
                            {
                                diffString += $"\\nbehind: {node.DiffFrom.Behind} commits";
                            }
                        }
                        else
                        {
                            diffString = "\\nlatest";
                        }
                    }
                    await writer.WriteLineAsync($"    {GetGraphVizNodeName(node)}[label=\"{GetSimpleRepoName(node.RepoUri)}\\n{node.Commit.Substring(0, 5)}{buildString}{diffString}\"];");
                    foreach (DependencyGraphNode childNode in node.Children)
                    {
                        await writer.WriteLineAsync($"    {GetGraphVizNodeName(node)} -> {GetGraphVizNodeName(childNode)}");
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
        private void LogIncoherencies(DependencyGraph graph)
        {
            if (!graph.IncoherentNodes.Any() || !_options.IncludeCoherency)
            {
                return;
            }
            Console.WriteLine("Incoherent Dependencies:");
            foreach (DependencyDetail incoherentDependency in graph.IncoherentDependencies)
            {
                Console.WriteLine($"  - Repo:    {incoherentDependency.RepoUri}");
                Console.WriteLine($"    Commit:  {incoherentDependency.Commit}");
                Console.WriteLine($"    Name:    {incoherentDependency.Name}");
                Console.WriteLine($"    Version: {incoherentDependency.Version}");
                if (_options.IncludeToolset)
                {
                    Console.WriteLine($"    Type:    {incoherentDependency.Type}");
                }
            }

            Console.WriteLine("Incoherent Repositories:");
            foreach (DependencyGraphNode incoherentRoot in graph.IncoherentNodes)
            {
                LogIncoherentPath(incoherentRoot, null, "  ");
            }
        }

        private void LogIncoherentPath(DependencyGraphNode currentNode, DependencyGraphNode childNode, string indent)
        {
            LogBasicNodeDetails(currentNode, indent);
            foreach (DependencyGraphNode parentNode in currentNode.Parents)
            {
                LogIncoherentPath(parentNode, currentNode, indent + "  ");
            }
        }

        /// <summary>
        ///     Log an individual dependency graph node.
        /// </summary>
        /// <param name="node">Node to log</param>
        /// <param name="indent">Current indentation level.</param>
        private void LogDependencyGraphNode(DependencyGraphNode node, string indent)
        {
            // Log the repository information.
            LogBasicNodeDetails(node, indent);
            if (node.Dependencies != null && node.Dependencies.Any())
            {
                Console.WriteLine($"{indent}  Dependencies:");

                // Log the dependencies at this repository.
                foreach (DependencyDetail dependency in node.Dependencies)
                {
                    Console.WriteLine($"{indent}  - Name:    {dependency.Name}");
                    Console.WriteLine($"{indent}    Version: {dependency.Version}");
                    if (_options.IncludeToolset)
                    {
                        Console.WriteLine($"{indent}    Type:    {dependency.Type}");
                    }
                }
            }

            if (node.Children.Any())
            {
                Console.WriteLine($"{indent}  Input Repositories:");

                // Walk children
                foreach (DependencyGraphNode childNode in node.Children)
                {
                    LogDependencyGraphNode(childNode, $"{indent}  ");
                }
            }
        }
    }
}
