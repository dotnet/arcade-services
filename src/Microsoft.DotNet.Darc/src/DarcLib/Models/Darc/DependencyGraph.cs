// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    public enum NodeDiff
    {
        /// <summary>
        ///     No node diff done
        /// </summary>
        None,
        /// <summary>
        ///     Diff each node from the latest build of each repo in the graph.
        ///     The latest build of all nodes is chosen.
        /// </summary>
        LatestInGraph,
        /// <summary>
        ///     Diff each node from the latest build in the channel that the node's
        ///     build was applied to.
        ///     
        ///     Generally this will give good results, though results may be confusing if some
        ///     nodes in the graph came from builds applied to different channel.
        /// </summary>
        LatestInChannel,
    }

    public enum EarlyBreakOnType
    {
        /// <summary>
        /// Do not break graph build early
        /// </summary>
        None,
        /// <summary>
        /// Break graph when all the specified dependencies have
        /// been found
        /// </summary>
        Dependencies,
        /// <summary>
        /// Break when the specified assets have been found
        /// </summary>
        Assets
    }

    public class EarlyBreakOn
    {
        /// <summary>
        ///     Do not break early
        /// </summary>
        public static readonly EarlyBreakOn NoEarlyBreak = new EarlyBreakOn() { Type = EarlyBreakOnType.None };

        /// <summary>
        ///     When should early breaking be done (how should the BreakOn list be interpreted)
        /// </summary>
        public EarlyBreakOnType Type { get; set; }
        /// <summary>
        ///     When all the elements in BreakOn have been found,
        ///     (interpreted based on Type), break the graph build.
        /// </summary>
        public List<string> BreakOn { get; set; }
    }

    public class DependencyGraphBuildOptions
    {
        /// <summary>
        /// Include toolset dependencies in the build.
        /// </summary>
        public bool IncludeToolset { get; set; }

        /// <summary>
        /// Lookup build information for each node. Only valid for remote builds.
        /// </summary>
        public bool LookupBuilds { get; set; }
        
        /// <summary>
        /// Type of node diff to perform. Local build only supports 'None' 
        /// </summary>
        public NodeDiff NodeDiff { get; set; } = NodeDiff.None;
        
        /// <summary>
        /// Stop the graph build based on the provided options
        /// </summary>
        public EarlyBreakOn EarlyBuildBreak { get; set; } = EarlyBreakOn.NoEarlyBreak;

        /// <summary>
        ///     If true, cycles are computed as part of the graph build
        ///     if cycles are encountered, and the Cycles member of DependencyGraph
        ///     will be non-empty
        /// </summary>
        public bool ComputeCyclePaths { get; set; } = true;

        public string GitExecutable { get; set; } = "git";
    }

    public class DependencyGraph
    {
        private static Dictionary<string, string> _remotesMapping = null;

        public DependencyGraph(
            DependencyGraphNode root,
            IEnumerable<DependencyDetail> uniqueDependencies,
            IEnumerable<DependencyDetail> incoherentDependencies,
            IEnumerable<DependencyGraphNode> allNodes,
            IEnumerable<DependencyGraphNode> incoherentNodes,
            IEnumerable<Build> contributingBuilds,
            IEnumerable<DependencyDetail> dependenciesMissingBuilds,
            IEnumerable<IEnumerable<DependencyGraphNode>> cycles)
        {
            Root = root;
            UniqueDependencies = uniqueDependencies;
            Nodes = allNodes;
            IncoherentNodes = incoherentNodes;
            IncoherentDependencies = incoherentDependencies;
            ContributingBuilds = contributingBuilds;
            DependenciesMissingBuilds = dependenciesMissingBuilds;
            Cycles = cycles;
        }

        public DependencyGraphNode Root { get; set; }

        public IEnumerable<DependencyDetail> UniqueDependencies { get; set; }

        /// <summary>
        ///     Incoherent dependencies in the graph.
        ///     This is the list of dependencies that have the same name but different versions.
        ///     This list could contain more incoherencies than the other incoherent nodes list,
        ///     if multiple builds were done of the same sha in a repo.
        /// </summary>
        public IEnumerable<DependencyDetail> IncoherentDependencies { get; set; }

        /// <summary>
        ///     All nodes in the graph (unique repo+sha combinations)
        /// </summary>
        public IEnumerable<DependencyGraphNode> Nodes { get; set; }

        /// <summary>
        ///     Incoherent nodes in the graph.
        ///     Incoherencies are cases where the same repository appears multiple times in the graph
        ///     at different commits. For instance, if two different versions of core-setup appear in the graph,
        ///     these are incoherent nodes.
        /// </summary>
        public IEnumerable<DependencyGraphNode> IncoherentNodes { get; set; }

        /// <summary>
        ///     Builds that contributed dependencies to the graph.
        /// </summary>
        public IEnumerable<Build> ContributingBuilds { get; set; }

        /// <summary>
        ///     Dependencies in the graph for which a corresponding build could not
        ///     be found.
        /// </summary>
        public IEnumerable<DependencyDetail> DependenciesMissingBuilds { get; set; }

        /// <summary>
        ///     A list of cycles.  Each cycle is represented as a list of nodes
        ///     in the cycle.  The "topmost" node (closest to root of the graph) is the first node.
        /// </summary>
        public IEnumerable<IEnumerable<DependencyGraphNode>> Cycles { get; set; }

        /// <summary>
        ///     Builds a dependency graph given a root repo and commit using remotes.
        /// </summary>
        /// <param name="remoteFactory">Factory that can create remotes based on repo uris</param>
        /// <param name="repoUri">Root repository URI</param>
        /// <param name="commit">Root commit</param>
        /// <param name="options">Graph build options.</param>
        /// <param name="logger">Logger</param>
        /// <returns>New dependency graph.</returns>
        public static async Task<DependencyGraph> BuildRemoteDependencyGraphAsync(
            IRemoteFactory remoteFactory,
            string repoUri,
            string commit,
            DependencyGraphBuildOptions options,
            ILogger logger)
        {
            return await BuildDependencyGraphImplAsync(
                remoteFactory,
                null, /* no initial root dependencies */
                repoUri,
                commit,
                options,
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
        /// <param name="options">Graph build options.</param>
        /// <param name="logger">Logger</param>
        /// <returns>New dependency graph.</returns>
        public static async Task<DependencyGraph> BuildRemoteDependencyGraphAsync(
            IRemoteFactory remoteFactory,
            IEnumerable<DependencyDetail> rootDependencies,
            string repoUri,
            string commit,
            DependencyGraphBuildOptions options,
            ILogger logger)
        {
            return await BuildDependencyGraphImplAsync(
                remoteFactory,
                rootDependencies,
                repoUri,
                commit,
                options,
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
        /// <param name="options">Graph build options</param>
        /// <param name="logger">Logger</param>
        /// <param name="testPath">If running unit tests, commits will be looked up as folders under this path</param>
        /// <param name="remotesMap">Map of remote uris to local paths</param>
        /// <param name="reposFolder">Folder containing local repositories.</param>
        /// <returns>New dependency graph.</returns>
        public static async Task<DependencyGraph> BuildLocalDependencyGraphAsync(
            IEnumerable<DependencyDetail> rootDependencies,
            DependencyGraphBuildOptions options,
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
                options,
                false,
                logger,
                reposFolder,
                remotesMap,
                testPath);
        }

        /// <summary>
        ///     Validate that the graph build options are correct.
        /// </summary>
        /// <param name="remoteFactory"></param>
        /// <param name="rootDependencies"></param>
        /// <param name="repoUri"></param>
        /// <param name="commit"></param>
        /// <param name="options"></param>
        /// <param name="remote"></param>
        /// <param name="logger"></param>
        /// <param name="reposFolder"></param>
        /// <param name="remotesMap"></param>
        /// <param name="testPath"></param>
        private static void ValidateBuildOptions(
            IRemoteFactory remoteFactory,
            IEnumerable<DependencyDetail> rootDependencies,
            string repoUri,
            string commit,
            DependencyGraphBuildOptions options,
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

            if (rootDependencies != null && !rootDependencies.Any())
            {
                throw new DarcException("Root dependencies were not supplied.");
            }

            if (!remote)
            {
                if (options.LookupBuilds)
                {
                    throw new DarcException("Build lookup only available in remote build mode.");
                }
                if (options.NodeDiff != NodeDiff.None)
                {
                    throw new DarcException($"Node diff type '{options.NodeDiff}' only available in remote build mode.");
                }
            }
            else
            {
                if (options.NodeDiff != NodeDiff.None && !options.LookupBuilds)
                {
                    throw new DarcException("Node diff requires build lookup.");
                }
            }
        }

        private static async Task DoLatestInChannelGraphNodeDiffAsync(
            IRemoteFactory remoteFactory,
            ILogger logger,
            Dictionary<string, DependencyGraphNode> nodeCache,
            Dictionary<string, DependencyGraphNode> visitedRepoUriNodes)
        {
            logger.LogInformation("Running latest in channel node diff.");

            IRemote barOnlyRemote = await remoteFactory.GetBarOnlyRemoteAsync(logger);

            // Walk each node in the graph and diff against the latest build in the channel
            // that was also applied to the node.
            Dictionary<string, string> latestCommitCache = new Dictionary<string, string>();
            foreach (DependencyGraphNode node in nodeCache.Values)
            {
                // Start with an unknown diff.
                node.DiffFrom = GitDiff.UnknownDiff();

                if (node.ContributingBuilds.Any())
                {
                    // Choose latest build of node that has a channel.
                    Build newestBuildWithChannel = node.ContributingBuilds.OrderByDescending(b => b.DateProduced).FirstOrDefault(
                        b => b.Channels != null && b.Channels.Any());
                    // If no build was found (e.g. build was flowed without a channel or channel was removed from
                    // a build, then no diff from latest.
                    if (newestBuildWithChannel != null)
                    {
                        int channelId = newestBuildWithChannel.Channels.First().Id;
                        // Just choose the first channel. This algorithm is mostly just heuristic.
                        string latestCommitKey = $"{node.Repository}@{channelId}";
                        string latestCommit = null;
                        if (!latestCommitCache.TryGetValue(latestCommitKey, out latestCommit))
                        {
                            // Look up latest build in the channel
                            var latestBuild = await barOnlyRemote.GetLatestBuildAsync(node.Repository, channelId);
                            // Could be null, if the only build was removed from the channel
                            if (latestBuild != null)
                            {
                                latestCommit = latestBuild.Commit;
                            }
                            // Add to cache
                            latestCommitCache.Add(latestCommitKey, latestCommit);
                        }

                        // Perform diff if there is a latest commit.
                        if (!string.IsNullOrEmpty(latestCommit))
                        {
                            IRemote repoRemote = await remoteFactory.GetRemoteAsync(node.Repository, logger);
                            // This will return a no-diff if latestCommit == node.Commit
                            node.DiffFrom = await repoRemote.GitDiffAsync(node.Repository, latestCommit, node.Commit);
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Diff each node in the graph against the latest build in
        ///     the graph.
        /// </summary>
        /// <param name="remoteFactory"></param>
        /// <param name="logger"></param>
        /// <param name="nodeCache"></param>
        /// <param name="visitedRepoUriNodes"></param>
        /// <returns></returns>
        private static async Task DoLatestInGraphNodeDiffAsync(
            IRemoteFactory remoteFactory,
            ILogger logger,
            Dictionary<string, DependencyGraphNode> nodeCache,
            Dictionary<string, DependencyGraphNode> visitedRepoUriNodes)
        {
            logger.LogInformation("Running latest in graph node diff.");

            // Find the build of each repo in the graph, then
            // get the diff info from the latest
            foreach (string repo in visitedRepoUriNodes.Keys)
            {
                // Get all nodes with this value
                List<DependencyGraphNode> nodes = nodeCache.Values.Where(n => n.Repository == repo).ToList();
                // If only one, determine latest
                if (nodes.Count > 1)
                {
                    // Find latest
                    DependencyGraphNode newestNode = null;
                    Build newestBuild = null;
                    foreach (DependencyGraphNode node in nodes)
                    {
                        if (newestNode == null)
                        {
                            newestNode = node;
                            if (newestNode.ContributingBuilds.Any())
                            {
                                newestBuild = newestNode.ContributingBuilds.OrderByDescending(b => b.DateProduced).First();
                            }
                        }
                        else if (node.ContributingBuilds.Any(b => b.DateProduced > newestBuild?.DateProduced))
                        {
                            newestNode = node;
                            newestBuild = newestNode.ContributingBuilds.OrderByDescending(b => b.DateProduced).First();
                        }
                    }

                    // Compare all other nodes to the latest
                    foreach (DependencyGraphNode node in nodes)
                    {
                        IRemote repoRemote = await remoteFactory.GetRemoteAsync(node.Repository, logger);
                        // If node == newestNode, returns no diff.
                        node.DiffFrom = await repoRemote.GitDiffAsync(node.Repository, newestNode.Commit, node.Commit);
                    }
                }
                else
                {
                    DependencyGraphNode singleNode = nodes.Single();
                    singleNode.DiffFrom = GitDiff.NoDiff(singleNode.Commit);
                }
            }
        }

        /// <summary>
        ///     Creates a new dependency graph
        /// </summary>
        /// <param name="remoteFactory">Remote for factory for obtaining remotes to</param>
        /// <param name="rootDependencies">Root set of dependencies.  If null, then repoUri and commit should be set</param>
        /// <param name="repoUri">Root repository uri.  Must be valid if no root dependencies are passed.</param>
        /// <param name="commit">Root commit.  Must be valid if no root dependencies were passed.</param>
        /// <param name="includeToolset">If true, toolset dependencies are included.</param>
        /// <param name="lookupBuilds">If true, the builds contributing to each node are looked up. Must be a remote build.</param>
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
            DependencyGraphBuildOptions options,
            bool remote,
            ILogger logger,
            string reposFolder,
            IEnumerable<string> remotesMap,
            string testPath)
        {
            ValidateBuildOptions(remoteFactory, rootDependencies, repoUri, commit, 
                options, remote, logger, reposFolder, remotesMap, testPath);

            if (rootDependencies != null)
            {
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

            AssetComparer assetEqualityComparer = new AssetComparer();
            HashSet<Build> allContributingBuilds = null;
            HashSet<DependencyDetail> dependenciesMissingBuilds = null;
            HashSet<Build> rootNodeBuilds = null;
            Dictionary<DependencyDetail, Build> dependencyCache =
                new Dictionary<DependencyDetail, Build>(new DependencyDetailComparer());
            List<LinkedList<DependencyGraphNode>> cycles = new List<LinkedList<DependencyGraphNode>>();

            EarlyBreakOnType breakOnType = options.EarlyBuildBreak.Type;
            HashSet<string> breakOn = null;
            if (breakOnType != EarlyBreakOnType.None)
            {
                breakOn = new HashSet<string>(options.EarlyBuildBreak.BreakOn, StringComparer.OrdinalIgnoreCase);
            }

            if (options.LookupBuilds)
            {
                allContributingBuilds = new HashSet<Build>(new BuildComparer());
                dependenciesMissingBuilds = new HashSet<DependencyDetail>(new DependencyDetailComparer());
                rootNodeBuilds = new HashSet<Build>(new BuildComparer());

                // Look up the dependency and get the creating build.
                IRemote barOnlyRemote = await remoteFactory.GetBarOnlyRemoteAsync(logger);
                IEnumerable<Build> potentialRootNodeBuilds = await barOnlyRemote.GetBuildsAsync(repoUri, commit);
                // Filter by those actually producing the root dependencies, if they were supplied
                if (rootDependencies != null)
                {
                    potentialRootNodeBuilds = potentialRootNodeBuilds.Where(b =>
                        b.Assets.Any(a => rootDependencies.Any(d => assetEqualityComparer.Equals(a, d))));
                }
                // It's entirely possible that the root has no builds (e.g. change just checked in).
                // Don't record those. Instead, users of the graph should just look at the
                // root node's contributing builds and determine whether it's empty or not.
                foreach (Build build in potentialRootNodeBuilds)
                {
                    allContributingBuilds.Add(build);
                    rootNodeBuilds.Add(build);
                    AddAssetsToBuildCache(build, dependencyCache, breakOnType, breakOn);
                }
            }

            // Create the root node and add the repo to the visited bit vector.
            DependencyGraphNode rootGraphNode = new DependencyGraphNode(repoUri, commit, rootDependencies, rootNodeBuilds);
            rootGraphNode.VisitedNodes.Add(repoUri);
            // Nodes to visit is a queue, so that the evaluation order
            // of the graph is breadth first.
            Queue<DependencyGraphNode> nodesToVisit = new Queue<DependencyGraphNode>();
            nodesToVisit.Enqueue(rootGraphNode);
            HashSet<DependencyDetail> uniqueDependencyDetails;

            if (rootGraphNode.Dependencies != null)
            {
                uniqueDependencyDetails = new HashSet<DependencyDetail>(
                    rootGraphNode.Dependencies,
                    new DependencyDetailComparer());
                // Remove the dependencies details from the
                // break on if break on type is Dependencies
                if (breakOnType == EarlyBreakOnType.Dependencies)
                {
                    rootGraphNode.Dependencies.Select(d => breakOn.Remove(d.Name));
                }
            }
            else
            {
                uniqueDependencyDetails = new HashSet<DependencyDetail>(
                    new DependencyDetailComparer());
            }

            // If we already found the assets/dependencies we wanted, clear the
            // visit list and we'll drop through.
            if (breakOnType != EarlyBreakOnType.None && breakOn.Count == 0)
            {
                logger.LogInformation($"Stopping graph build after finding all assets/dependencies.");
                nodesToVisit.Clear();
            }

            // Cache of nodes we've visited. If we reach a repo/commit combo already in the cache,
            // we can just add these nodes as a child. The cache key is '{repoUri}@{commit}'
            Dictionary<string, DependencyGraphNode> nodeCache = new Dictionary<string, DependencyGraphNode>();
            nodeCache.Add($"{rootGraphNode.Repository}@{rootGraphNode.Commit}", rootGraphNode);

            // Cache of incoherent nodes, looked up by repo URI.
            Dictionary<string, DependencyGraphNode> visitedRepoUriNodes = new Dictionary<string, DependencyGraphNode>();
            HashSet<DependencyGraphNode> incoherentNodes = new HashSet<DependencyGraphNode>();
            // Cache of incoherent dependencies, looked up by name
            Dictionary<string, DependencyDetail> incoherentDependenciesCache = new Dictionary<string, DependencyDetail>();
            HashSet<DependencyDetail> incoherentDependencies = new HashSet<DependencyDetail>();

            while (nodesToVisit.Count > 0)
            {
                DependencyGraphNode node = nodesToVisit.Dequeue();

                logger.LogInformation($"Visiting {node.Repository}@{node.Commit}");

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
                    logger.LogInformation($"Getting dependencies at {node.Repository}@{node.Commit}");

                    dependencies = await GetDependenciesAsync(
                        remoteFactory,
                        remote,
                        logger,
                        options.GitExecutable,
                        node.Repository,
                        node.Commit,
                        options.IncludeToolset,
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
                                $"{node.Repository}@{node.Commit} " +
                                $"is missing repository uri or commit information, skipping");
                            continue;
                        }

                        // If the dependency's repo uri has been traversed, we've reached a cycle in this subgraph
                        // and should break.
                        if (node.VisitedNodes.Contains(dependency.RepoUri))
                        {
                            logger.LogInformation($"Node {node.Repository}@{node.Commit} " +
                                $"introduces a cycle to {dependency.RepoUri}, skipping");

                            if (options.ComputeCyclePaths)
                            {
                                var newCycles = ComputeCyclePaths(node, dependency.RepoUri);
                                cycles.AddRange(newCycles);
                            }
                            continue;
                        }

                        // Add the individual dependency to the set of unique dependencies seen
                        // in the whole graph.
                        uniqueDependencyDetails.Add(dependency);
                        if (incoherentDependenciesCache.TryGetValue(dependency.Name, out DependencyDetail existingDependency))
                        {
                            incoherentDependencies.Add(existingDependency);
                            incoherentDependencies.Add(dependency);
                        }
                        else
                        {
                            incoherentDependenciesCache.Add(dependency.Name, dependency);
                        }

                        HashSet<Build> nodeContributingBuilds = null;
                        if (options.LookupBuilds)
                        {
                            nodeContributingBuilds = new HashSet<Build>(new BuildComparer());
                            // Look up dependency in cache first
                            if (dependencyCache.TryGetValue(dependency, out Build existingBuild))
                            {
                                nodeContributingBuilds.Add(existingBuild);
                                allContributingBuilds.Add(existingBuild);
                            }
                            else
                            {
                                // Look up the dependency and get the creating build.
                                IRemote barOnlyRemote = await remoteFactory.GetBarOnlyRemoteAsync(logger);
                                IEnumerable<Build> potentiallyContributingBuilds = await barOnlyRemote.GetBuildsAsync(dependency.RepoUri, dependency.Commit);
                                // Filter by those actually producing the dependency. Most of the time this won't
                                // actually result in a different set of contributing builds, but should avoid any subtle bugs where
                                // there might be overlap between repos, or cases where there were multiple builds at the same sha.
                                potentiallyContributingBuilds = potentiallyContributingBuilds.Where(b =>
                                    b.Assets.Any(a => assetEqualityComparer.Equals(a, dependency)));
                                if (!potentiallyContributingBuilds.Any())
                                {
                                    // Couldn't find a build that produced the dependency.
                                    dependenciesMissingBuilds.Add(dependency);
                                }
                                else
                                {
                                    foreach (Build build in potentiallyContributingBuilds)
                                    {
                                        allContributingBuilds.Add(build);
                                        nodeContributingBuilds.Add(build);
                                        AddAssetsToBuildCache(build, dependencyCache, breakOnType, breakOn);
                                    }
                                }
                            }
                        }

                        // We may have visited this node before.  If so, add it as a child and avoid additional walks.
                        // Update the list of contributing builds.
                        if (nodeCache.TryGetValue($"{dependency.RepoUri}@{dependency.Commit}", out DependencyGraphNode existingNode))
                        {
                            if (options.LookupBuilds)
                            {
                                // Add the contributing builds. It's possible that
                                // different dependencies on a single node (repo/sha) were produced
                                // from multiple builds
                                foreach (Build build in nodeContributingBuilds)
                                {
                                    existingNode.ContributingBuilds.Add(build);
                                }
                            }
                            logger.LogInformation($"Node {dependency.RepoUri}@{dependency.Commit} has already been created, adding as child");
                            node.AddChild(existingNode, dependency);
                            continue;
                        }

                        // Otherwise, create a new node for this dependency.
                        DependencyGraphNode newNode = new DependencyGraphNode(
                            dependency.RepoUri,
                            dependency.Commit,
                            null,
                            node.VisitedNodes,
                            nodeContributingBuilds);
                        
                        // Cache the dependency and add it to the visitation stack.
                        nodeCache.Add($"{dependency.RepoUri}@{dependency.Commit}", newNode);
                        nodesToVisit.Enqueue(newNode);
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
                            visitedRepoUriNodes.Add(newNode.Repository, newNode);
                        }

                        // If breaking on dependencies, then decide whether we need to break
                        // here.
                        if (breakOnType == EarlyBreakOnType.Dependencies)
                        {
                            breakOn.Remove(dependency.Name);
                        }

                        if (breakOnType != EarlyBreakOnType.None && breakOn.Count == 0)
                        {
                            logger.LogInformation($"Stopping graph build after finding all assets/dependencies.");
                            nodesToVisit.Clear();
                            break;
                        }
                    }
                }
            }

            switch(options.NodeDiff)
            {
                case NodeDiff.None:
                    // Nothing
                    break;
                case NodeDiff.LatestInGraph:
                    await DoLatestInGraphNodeDiffAsync(remoteFactory, logger, nodeCache, visitedRepoUriNodes);
                    break;
                case NodeDiff.LatestInChannel:
                    await DoLatestInChannelGraphNodeDiffAsync(remoteFactory, logger, nodeCache, visitedRepoUriNodes);
                    break;
            }

            return new DependencyGraph(rootGraphNode,
                                       uniqueDependencyDetails,
                                       incoherentDependencies,
                                       nodeCache.Values,
                                       incoherentNodes,
                                       allContributingBuilds,
                                       dependenciesMissingBuilds,
                                       cycles);
        }

        /// <summary>
        ///     Given that the <paramref name="currentNode"/> introduces one or more cycles to <paramref name="repoCycleRoot"/>
        ///     compute the cycles that it introduces.
        /// </summary>
        /// <param name="currentNode">Current node</param>
        /// <param name="repoCycleRoot">Repo uri of dependency introducing cycle</param>
        /// <returns>List of cycles</returns>
        /// <remarks></remarks>
        private static List<LinkedList<DependencyGraphNode>> ComputeCyclePaths(
            DependencyGraphNode currentNode, string repoCycleRoot)
        {
            List<LinkedList<DependencyGraphNode>> allCyclesRootedAtNode = new List<LinkedList<DependencyGraphNode>>();

            // Find all parents who have a path to the root node.  This set might also
            // be the root node, since the root node has itself marked in VisitedNodes.
            // After reaching the root along all paths, this set will be empty
            var parentsInCycles = currentNode.Parents.Where(p => p.VisitedNodes.Contains(repoCycleRoot));

            if (parentsInCycles.Any())
            {
                // Recurse into each parent, combining the returned cycles together and
                // appending on the current node.
                foreach (var parent in parentsInCycles)
                {
                    var cyclesRootedAtParentNode = ComputeCyclePaths(parent, repoCycleRoot);
                    foreach (var cycleRootedAtNode in cyclesRootedAtParentNode)
                    {
                        cycleRootedAtNode.AddLast(currentNode);
                        allCyclesRootedAtNode.Add(cycleRootedAtNode);
                    }
                }
            }
            else
            {
                // Create a segment containing just this node and return that
                LinkedList<DependencyGraphNode> newCycleSegment = new LinkedList<DependencyGraphNode>();
                allCyclesRootedAtNode.Add(newCycleSegment);
                newCycleSegment.AddFirst(currentNode);
            }

            return allCyclesRootedAtNode;
        }

        /// <summary>
        ///     Add the assets from each build to the cache.
        ///     Also evaluate whether we see any of the assets that we are supposed to break
        ///     on, and remove them from the break on set if so.
        /// </summary>
        /// <param name="build">Build producing assets</param>
        /// <param name="dependencyCache">Dependency cache</param>
        /// <param name="earlyBreakOnType">Early break on type</param>
        /// <param name="breakOn">Hash set of assets. Any assets in the <paramref name="build"/>
        ///     that exist in <paramref name="breakOn"/> will be removed from
        ///     <paramref name="breakOn"/> if <paramref name="earlyBreakOnType"/> is "Assets"</param>
        private static void AddAssetsToBuildCache(
            Build build, 
            Dictionary<DependencyDetail, Build> dependencyCache,
            EarlyBreakOnType earlyBreakOnType,
            HashSet<string> breakOn)
        {
            foreach (Asset buildAsset in build.Assets)
            {
                DependencyDetail newDependency =
                    new DependencyDetail() { Name = buildAsset.Name, Version = buildAsset.Version, Commit = build.Commit };
                // Possible that the same asset could be listed multiple times in a build, so avoid accidentally adding
                // things multiple times
                if (!dependencyCache.ContainsKey(newDependency))
                {
                    dependencyCache.Add(newDependency, build);
                }

                if (earlyBreakOnType == EarlyBreakOnType.Assets)
                {
                    breakOn.Remove(buildAsset.Name);
                }
            }
        }

        private static string GetRepoPath(
            string repoUri,
            string commit,
            IEnumerable<string> remotesMap,
            string reposFolder,
            ILogger logger,
            string gitExecutable)
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
                    string parent = LocalHelpers.GetRootDir(gitExecutable, logger);
                    folder = Directory.GetParent(parent).FullName;
                }

                repoPath = LocalHelpers.GetRepoPathFromFolder(gitExecutable, folder, commit, logger);

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
            string gitExecutable,
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
                        Local local = new Local(logger, testPath);
                        dependencies = await local.GetDependenciesAsync();
                    }
                }
                else if (remote)
                {
                    IRemote remoteClient = await remoteFactory.GetRemoteAsync(repoUri, logger);
                    dependencies = await remoteClient.GetDependenciesAsync(
                        repoUri, 
                        commit);
                }
                else
                {
                    string repoPath = GetRepoPath(repoUri, commit, remotesMap, reposFolder, logger, gitExecutable);

                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        Local local = new Local(logger);
                        string fileContents = LocalHelpers.GitShow(
                            gitExecutable,
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
            catch (DependencyFileNotFoundException)
            {
                // This is not an error. Dependencies can be specified with explicit shas that
                // may not have eng/Version.Details.xml at that point.
                logger.LogWarning($"{repoUri}@{commit} does not have an eng/Version.Details.xml.");
                return null;
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
