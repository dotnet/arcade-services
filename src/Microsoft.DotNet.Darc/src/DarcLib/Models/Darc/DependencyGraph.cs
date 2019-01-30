// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    public class DependencyGraph
    {
        private static Dictionary<string, string> _remotesMapping = null;

        public DependencyGraph(
            DependencyGraphNode root, 
            IEnumerable<DependencyDetail> uniqueDependencies,
            IEnumerable<DependencyGraphNode> allNodes,
            IEnumerable<DependencyGraphNode> incoherentNodes)
        {
            Root = root;
            UniqueDependencies = uniqueDependencies;
            Nodes = allNodes;
            IncoherentNodes = incoherentNodes;
        }

        public DependencyGraphNode Root { get; set; }

        public IEnumerable<DependencyDetail> UniqueDependencies { get; set; }

        /// <summary>
        ///     All nodes in the graph (unique repo+sha combinations)
        /// </summary>
        public IEnumerable<DependencyGraphNode> Nodes { get; set; }

        public IEnumerable<DependencyGraphNode> IncoherentNodes { get; set; }

        /// <summary>
        ///     Builds a dependency graph given a root repo and commit using remotes.
        /// </summary>
        /// <param name="remoteFactory">Factory that can create remotes based on repo uris</param>
        /// <param name="repoUri">Root repository URI</param>
        /// <param name="commit">Root commit</param>
        /// <param name="includeToolset">If true, toolset dependencies are included.</param>
        /// <param name="logger">Logger</param>
        /// <returns>New dependency graph.</returns>
        public static async Task<DependencyGraph> BuildRemoteDependencyGraphAsync(
            IRemoteFactory remoteFactory,
            string repoUri,
            string commit,
            bool includeToolset,
            ILogger logger)
        {
            return await BuildDependencyGraphImplAsync(
                remoteFactory,
                null, /* no initial root dependencies */
                repoUri,
                commit,
                includeToolset,
                true,
                logger,
                null,
                null,
                null);
        }

        /// <summary>
        ///     Builds a dependency graph given a root repo and commit.
        /// </summary>
        /// <param name="remoteFactory">Factory that can create remotes based on repo uris</param>
        /// <param name="rootDependencies">Root set of dependencies</param>
        /// <param name="repoUri">Root repository URI</param>
        /// <param name="commit">Root commit</param>
        /// <param name="includeToolset">If true, toolset dependencies are included.</param>
        /// <param name="logger">Logger</param>
        /// <returns>New dependency graph.</returns>
        public static async Task<DependencyGraph> BuildRemoteDependencyGraphAsync(
            IRemoteFactory remoteFactory,
            IEnumerable<DependencyDetail> rootDependencies,
            string repoUri,
            string commit,
            bool includeToolset,
            ILogger logger)
        {
            return await BuildDependencyGraphImplAsync(
                remoteFactory,
                rootDependencies,
                repoUri,
                commit,
                includeToolset,
                true,
                logger,
                null,
                null,
                null);
        }

        /// <summary>
        ///     Builds a dependency graph using only local resources
        /// </summary>
        /// <param name="remoteFactory">Factory that can create remotes based on repo uris</param>
        /// <param name="rootDependencies">Root set of dependencies</param>
        /// <param name="rootRepoFolder">Root repository folder</param>
        /// <param name="rootRepoCommit">Root commit</param>
        /// <param name="includeToolset">If true, toolset dependencies are included.</param>
        /// <param name="logger">Logger</param>
        /// <param name="testPath">If running unit tests, commits will be looked up as folders under this path</param>
        /// <param name="remotesMap">Map of remote uris to local paths</param>
        /// <param name="reposFolder">Folder containing local repositories.</param>
        /// <returns>New dependency graph.</returns>
        public static async Task<DependencyGraph> BuildLocalDependencyGraphAsync(
            IEnumerable<DependencyDetail> rootDependencies,
            bool includeToolset,
            ILogger logger,
            string rootRepoFolder,
            string rootRepoCommit,
            string reposFolder,
            IEnumerable<string> remotesMap,
            string testPath = null)
        {
            return await BuildDependencyGraphImplAsync(
                null,
                rootDependencies,
                rootRepoFolder,
                rootRepoCommit,
                includeToolset,
                false,
                logger,
                reposFolder,
                remotesMap,
                testPath);
        }

        /// <summary>
        ///     Creates a new dependency graph
        /// </summary>
        /// <param name="remoteFactory">Remote for factory for obtaining remotes to</param>
        /// <param name="rootDependencies">Root set of dependencies.  If null, then repoUri and commit should be set</param>
        /// <param name="repoUri">Root repository uri.  Must be valid if no root dependencies are passed.</param>
        /// <param name="commit">Root commit.  Must be valid if no root dependencies were passed.</param>
        /// <param name="includeToolset">If true, toolset dependencies are included.</param>
        /// <param name="remote">If true, remote graph build is used.</param>
        /// <param name="logger">Logger</param>
        /// <param name="reposFolder">Path to repos</param>
        /// <param name="remotesMap">Map of remotes (e.g. https://github.com/dotnet/corefx) to folders</param>
        /// <param name="testPath">If running unit tests, commits will be looked up as folders under this path</param>
        /// <returns>New dependency graph</returns>
        private static async Task<DependencyGraph> BuildDependencyGraphImplAsync(
            IRemoteFactory remoteFactory,
            IEnumerable<DependencyDetail> rootDependencies,
            string repoUri,
            string commit,
            bool includeToolset,
            bool remote,
            ILogger logger,
            string reposFolder,
            IEnumerable<string> remotesMap,
            string testPath)
        {
            // Fail fast if darcSettings is null in a remote scenario
            if (remote && remoteFactory == null)
            {
                throw new DarcException("Remote graph build requires a remote factory.");
            }

            if (rootDependencies != null)
            {
                if (!rootDependencies.Any())
                {
                    throw new DarcException($"Root dependencies were not supplied.");
                }

                logger.LogInformation($"Starting build of graph from {rootDependencies.Count()} root dependencies " +
                    $"({repoUri}@{commit})");
                foreach (DependencyDetail dependency in rootDependencies)
                {
                    logger.LogInformation($"  {dependency.Name}@{dependency.Version}");
                }
            }
            else
            {
                logger.LogInformation($"Starting build of graph from ({repoUri}@{commit})");
            }

            // Create the root node and add the repo to the visited bit vector.
            DependencyGraphNode rootGraphNode = new DependencyGraphNode(repoUri, commit, rootDependencies);
            rootGraphNode.VisitedNodes.Add(repoUri);
            Stack<DependencyGraphNode> nodesToVisit = new Stack<DependencyGraphNode>();
            nodesToVisit.Push(rootGraphNode);
            HashSet<DependencyDetail> uniqueDependencyDetails;
            if (rootGraphNode.Dependencies != null)
            {
                uniqueDependencyDetails = new HashSet<DependencyDetail>(
                    rootGraphNode.Dependencies,
                    new DependencyDetailComparer());
            }
            else
            {
                uniqueDependencyDetails = new HashSet<DependencyDetail>(
                    new DependencyDetailComparer());
            }

            // Cache of nodes we've visited. If we reach a repo/commit combo already in the cache,
            // we can just add these nodes as a child. The cache key is '{repoUri}@{commit}'
            Dictionary<string, DependencyGraphNode> nodeCache = new Dictionary<string, DependencyGraphNode>();
            nodeCache.Add($"{rootGraphNode.RepoUri}@{rootGraphNode.Commit}", rootGraphNode);

            // Cache of incoherent nodes, looked up by repo URI.
            Dictionary<string, DependencyGraphNode> visitedRepoUriNodes = new Dictionary<string, DependencyGraphNode>();
            HashSet<DependencyGraphNode> incoherentNodes = new HashSet<DependencyGraphNode>();

            while (nodesToVisit.Count > 0)
            {
                DependencyGraphNode node = nodesToVisit.Pop();

                logger.LogInformation($"Visiting {node.RepoUri}@{node.Commit}");

                IEnumerable<DependencyDetail> dependencies;
                // In case of the root node which is initially put on the stack,
                // we already have the set of dependencies to start at (this may have been
                // filtered by the caller). So no need to get the dependencies again.
                if (node.Dependencies != null)
                {
                    dependencies = node.Dependencies;
                }
                else
                {
                    logger.LogInformation($"Getting dependencies at {node.RepoUri}@{node.Commit}");

                    dependencies = await GetDependenciesAsync(
                        remoteFactory,
                        remote,
                        logger,
                        node.RepoUri,
                        node.Commit,
                        includeToolset,
                        remotesMap,
                        reposFolder,
                        testPath);
                    // Set the dependencies on the current node.
                    node.Dependencies = dependencies;
                }

                if (dependencies != null)
                {
                    foreach (DependencyDetail dependency in dependencies)
                    {
                        // If this dependency is missing information, then skip it.
                        if (string.IsNullOrEmpty(dependency.RepoUri) ||
                            string.IsNullOrEmpty(dependency.Commit))
                        {
                            logger.LogInformation($"Dependency {dependency.Name}@{dependency.Version} in " +
                                $"{node.RepoUri}@{node.Commit} " +
                                $"is missing repository uri or commit information, skipping");
                            continue;
                        }

                        // If the dependency's repo uri has been traversed, we've reached a cycle in this subgraph
                        // and should break.
                        if (node.VisitedNodes.Contains(dependency.RepoUri))
                        {
                            logger.LogInformation($"Node {node.RepoUri}@{node.Commit} " +
                                $"introduces a cycle to {dependency.RepoUri}, skipping");
                            continue;
                        }

                        // Add the individual dependency to the set of unique dependencies seen
                        // in the whole graph.
                        uniqueDependencyDetails.Add(dependency);

                        // We may have visited this node before.  If so, add it as a child and avoid additional walks.
                        if (nodeCache.TryGetValue($"{dependency.RepoUri}@{dependency.Commit}", out DependencyGraphNode existingNode))
                        {
                            logger.LogInformation($"Node {dependency.RepoUri}@{dependency.Commit} has been created, adding as child");
                            node.AddChild(existingNode, dependency);
                            continue;
                        }

                        // Otherwise, create a new node for this dependency.
                        DependencyGraphNode newNode = new DependencyGraphNode(
                            dependency.RepoUri,
                            dependency.Commit,
                            null,
                            node.VisitedNodes);
                        // Cache the dependency and add it to the visitation stack.
                        nodeCache.Add($"{dependency.RepoUri}@{dependency.Commit}", newNode);
                        nodesToVisit.Push(newNode);
                        newNode.VisitedNodes.Add(dependency.RepoUri);
                        node.AddChild(newNode, dependency);
                        // Calculate incoherencies. If we've not yet visited the repo uri, add the
                        // new node based on its repo uri. Otherwise, add both the new node and the visited
                        // node to the incoherent nodes.
                        if (visitedRepoUriNodes.TryGetValue(dependency.RepoUri, out DependencyGraphNode visitedNode))
                        {
                            incoherentNodes.Add(visitedNode);
                            incoherentNodes.Add(newNode);
                        }
                        else
                        {
                            visitedRepoUriNodes.Add(newNode.RepoUri, newNode);
                        }
                    }
                }
            }

            return new DependencyGraph(rootGraphNode, uniqueDependencyDetails, nodeCache.Values, incoherentNodes);
        }

        private static string GetRepoPath(
            string repoUri,
            string commit,
            IEnumerable<string> remotesMap,
            string reposFolder,
            ILogger logger)
        {
            string repoPath = null;

            if (remotesMap != null)
            {
                if (_remotesMapping == null)
                {
                    _remotesMapping = CreateRemotesMapping(remotesMap);
                }

                if (!_remotesMapping.ContainsKey(repoPath))
                {
                    throw new DarcException($"A key matching '{repoUri}' was not " +
                        $"found in the mapping. Please make sure to include it...");
                }

                repoPath = _remotesMapping[repoPath];
            }
            else
            {
                string folder = null;

                if (!string.IsNullOrEmpty(reposFolder))
                {
                    folder = reposFolder;
                }
                else
                {
                    // If a repo folder or a mapping was not set we use the current parent's 
                    // parent folder.
                    string gitDir = LocalHelpers.GetGitDir(logger);
                    string parent = Directory.GetParent(gitDir).FullName;
                    folder = Directory.GetParent(parent).FullName;
                }

                repoPath = LocalHelpers.GetRepoPathFromFolder(folder, commit, logger);

                if (string.IsNullOrEmpty(repoPath))
                {
                    throw new DarcException($"Commit '{commit}' was not found in any " +
                        $"folder in '{folder}'. Make sure a folder for '{repoUri}' exists " +
                        $"and it has all the latest changes...");
                }
            }

            return repoPath;
        }

        private static async Task<IEnumerable<DependencyDetail>> GetDependenciesAsync(
            IRemoteFactory remoteFactory,
            bool remote,
            ILogger logger,
            string repoUri,
            string commit,
            bool includeToolset,
            IEnumerable<string> remotesMap,
            string reposFolder,
            string testPath = null)
        {
            try
            {
                IEnumerable<DependencyDetail> dependencies = null;

                if (!string.IsNullOrEmpty(testPath))
                {
                    testPath = Path.Combine(
                                testPath,
                                repoUri,
                                commit);

                    if (Directory.Exists(testPath))
                    {
                        Local local = new Local(
                            Path.Combine(
                                testPath,
                                ".git"),
                            logger);
                        dependencies = await local.GetDependenciesAsync();
                    }
                }
                else if (remote)
                {
                    IRemote remoteClient = remoteFactory.GetRemote(repoUri, logger);
                    dependencies = await remoteClient.GetDependenciesAsync(
                        repoUri, 
                        commit);
                }
                else
                {
                    string repoPath = GetRepoPath(repoUri, commit, remotesMap, reposFolder, logger);

                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        // Local's constructor expects the repo's .git folder to be passed in. In this 
                        // particular case we could pass any folder under 'repoPath' or even a fake one 
                        // but we use .git to keep things consistent to what Local expects
                        Local local = new Local($"{repoPath}/.git", logger);
                        string fileContents = LocalHelpers.GitShow(
                            repoPath,
                            commit,
                            VersionFiles.VersionDetailsXml,
                            logger);
                        dependencies = local.GetDependenciesFromFileContents(fileContents);
                    }
                }

                if (!includeToolset)
                {
                    dependencies = dependencies.Where(dependency => dependency.Type != DependencyType.Toolset);
                }
                return dependencies;
            }
            catch (Exception exc)
            {
                logger.LogError(exc, $"Something failed while trying the fetch the " +
                    $"dependencies of repo '{repoUri}' at sha " +
                    $"'{commit}'");
                throw;
            }
        }

        private static Dictionary<string, string> CreateRemotesMapping(IEnumerable<string> remotesMap)
        {
            Dictionary<string, string> remotesMapping = new Dictionary<string, string>();

            foreach (string remotes in remotesMap)
            {
                string[] keyValuePairs = remotes.Split(';');

                foreach (string keyValue in keyValuePairs)
                {
                    string[] kv = keyValue.Split(',');
                    remotesMapping.Add(kv[0], kv[1]);
                }
            }

            return remotesMapping;
        }
    }
}
