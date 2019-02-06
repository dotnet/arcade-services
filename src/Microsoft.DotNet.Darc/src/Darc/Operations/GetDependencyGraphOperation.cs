// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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
                    // If the repo uri and version are set, then call the graph
                    // build operation based on those.  Both should be set in this case.
                    // If they are not set, then gather the initial set based on the local repository,
                    // and then call the graph build with that root set.

                    if (!string.IsNullOrEmpty(_options.RepoUri))
                    {
                        if (string.IsNullOrEmpty(_options.Version))
                        {
                            Logger.LogError("If --repo is set, --version should be supplied");
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
                            Logger.LogError("If --version is supplied, then --repo is required");
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

                    // Build graph
                    graph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
                        remoteFactory,
                        rootDependencies,
                        _options.RepoUri ?? LocalHelpers.GetGitDir(Logger),
                        _options.Version ?? LocalHelpers.GetGitCommit(Logger),
                        _options.IncludeToolset,
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

                    // Build graph using only local resources
                    graph = await DependencyGraph.BuildLocalDependencyGraphAsync(
                        rootDependencies,
                        _options.IncludeToolset,
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
                else if (_options.GraphViz)
                {
                    LogGraphViz(graph);
                }
                else
                {
                    LogDependencyGraph(graph);
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
        ///     Log the dependency graph as a simple flat list of repo/sha combinations
        ///     that contribute to this graph.
        /// </summary>
        /// <param name="graph">Graph to log</param>
        private void LogFlatDependencyGraph(DependencyGraph graph)
        {
            Console.WriteLine($"Repositories:");
            foreach (DependencyGraphNode node in graph.Nodes)
            {
                Console.WriteLine($"  - Repo:     {node.RepoUri}");
                Console.WriteLine($"    Commit:   {node.Commit}");
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

        private void LogGraphViz(DependencyGraph graph)
        {
            Console.WriteLine("digraph repositoryGraph {");
            Console.WriteLine("    node [shape=record]");
            foreach (DependencyGraphNode node in graph.Nodes)
            {
                Console.WriteLine($"    {GetGraphVizNodeName(node)}[label=\"{GetSimpleRepoName(node.RepoUri)}\\n{node.Commit.Substring(0, 5)}\"];");
                foreach (DependencyGraphNode childNode in node.Children)
                {
                    Console.WriteLine($"    {GetGraphVizNodeName(node)} -> {GetGraphVizNodeName(childNode)}");
                }
            }

            Console.WriteLine("}");
        }

        /// <summary>
        ///     Log incoherencies in the graph, places where repos appear at different shas,
        ///     or dependencies appear at different version numbers.
        /// </summary>
        /// <param name="graph">Graph to log incoherencies for</param>
        private void LogIncoherencies(DependencyGraph graph)
        {
            if (!graph.IncoherentNodes.Any())
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
            Console.WriteLine($"{indent}- Repo:    {currentNode.RepoUri}");
            Console.WriteLine($"{indent}  Commit:  {currentNode.Commit}");
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
            Console.WriteLine($"{indent}- Repo:    {node.RepoUri}");
            Console.WriteLine($"{indent}  Commit:  {node.Commit}");
            if (node.Dependencies.Any())
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
