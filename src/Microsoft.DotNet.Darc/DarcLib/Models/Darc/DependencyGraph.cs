// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

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
    /// Determine which dependencies are missing builds
    /// </summary>
    public bool ComputeMissingBuilds { get; set; }

    /// <summary>
    /// Type of node diff to perform. Local build only supports 'None' 
    /// </summary>
    public NodeDiff NodeDiff { get; set; } = NodeDiff.None;

    /// <summary>
    ///     If true, cycles are computed as part of the graph build
    ///     if cycles are encountered, and the Cycles member of DependencyGraph
    ///     will be non-empty
    /// </summary>
    public bool ComputeCyclePaths { get; set; } = false;

    /// <summary>
    ///     Location of git executable for use if any git commands need to be run.
    /// </summary>
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
        IEnumerable<IEnumerable<DependencyGraphNode>> cycles)
    {
        Root = root;
        UniqueDependencies = uniqueDependencies;
        Nodes = allNodes;
        IncoherentNodes = incoherentNodes;
        IncoherentDependencies = incoherentDependencies;
        ContributingBuilds = contributingBuilds;
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
        IBasicBarClient barClient,
        string repoUri,
        string commit,
        DependencyGraphBuildOptions options,
        ILogger logger)
    {
        return await BuildDependencyGraphImplAsync(
            remoteFactory,
            barClient,
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
        IBarApiClient barClient,
        IEnumerable<DependencyDetail> rootDependencies,
        string repoUri,
        string commit,
        DependencyGraphBuildOptions options,
        ILogger logger)
    {
        return await BuildDependencyGraphImplAsync(
            remoteFactory,
            barClient,
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
    private static void ValidateBuildOptions(
        IRemoteFactory remoteFactory,
        IEnumerable<DependencyDetail> rootDependencies,
        DependencyGraphBuildOptions options,
        bool remote)
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
        IBasicBarClient barClient,
        ILogger logger,
        Dictionary<string, DependencyGraphNode> nodeCache)
    {
        logger.LogInformation("Running latest in channel node diff.");

        // Walk each node in the graph and diff against the latest build in the channel
        // that was also applied to the node.
        var latestCommitCache = new Dictionary<string, string>();
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
                    var channelId = newestBuildWithChannel.Channels[0].Id;
                    // Just choose the first channel. This algorithm is mostly just heuristic.
                    var latestCommitKey = $"{node.Repository}@{channelId}";
                    if (!latestCommitCache.TryGetValue(latestCommitKey, out var latestCommit))
                    {
                        // Look up latest build in the channel
                        var latestBuild = await barClient.GetLatestBuildAsync(node.Repository, channelId);
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
                        IRemote repoRemote = await remoteFactory.CreateRemoteAsync(node.Repository);
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
        foreach (var repo in visitedRepoUriNodes.Keys)
        {
            // Get all nodes with this value
            var nodes = nodeCache.Values.Where(n => n.Repository == repo).ToList();
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
                    IRemote repoRemote = await remoteFactory.CreateRemoteAsync(node.Repository);
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
        IBasicBarClient barClient,
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
        var rootDependencyList = rootDependencies?.ToList();
        var remotesList = remotesMap?.ToList();
        ValidateBuildOptions(remoteFactory, rootDependencyList, options, remote);

        if (rootDependencies != null)
        {
            logger.LogInformation($"Starting build of graph from {rootDependencyList.Count} root dependencies " +
                                  $"({repoUri}@{commit})");
            foreach (DependencyDetail dependency in rootDependencyList)
            {
                logger.LogInformation($"  {dependency.Name}@{dependency.Version}");
            }
        }
        else
        {
            logger.LogInformation($"Starting build of graph from ({repoUri}@{commit})");
        }

        List<LinkedList<DependencyGraphNode>> cycles = [];
        Dictionary<string, List<Build>> buildLookups = null;

        if (options.LookupBuilds)
        {
            buildLookups = new Dictionary<string, List<Build>>
            {
                // Look up the dependency and get the creating build.
                { $"{repoUri}@{commit}", (await barClient.GetBuildsAsync(repoUri, commit)).ToList() }
            };
        }

        // Create the root node and add the repo to the visited bit vector.
        List<Build> allContributingBuilds = null;
        var rootGraphNode = new DependencyGraphNode(repoUri, commit, rootDependencyList, null);
        rootGraphNode.VisitedNodes.Add(repoUri);
        // Nodes to visit is a queue, so that the evaluation order
        // of the graph is breadth first.
        var nodesToVisit = new Queue<DependencyGraphNode>();
        nodesToVisit.Enqueue(rootGraphNode);
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
        var nodeCache = new Dictionary<string, DependencyGraphNode>
        {
            { $"{rootGraphNode.Repository}@{rootGraphNode.Commit}", rootGraphNode }
        };

        // Cache of incoherent nodes, looked up by repo URI.
        var visitedRepoUriNodes = new Dictionary<string, DependencyGraphNode>();
        var incoherentNodes = new HashSet<DependencyGraphNode>();
        // Cache of incoherent dependencies, looked up by name
        var incoherentDependenciesCache = new Dictionary<string, DependencyDetail>();
        var incoherentDependencies = new HashSet<DependencyDetail>(new LooseDependencyDetailComparer());

        while (nodesToVisit.Count > 0)
        {
            DependencyGraphNode node = nodesToVisit.Dequeue();

            logger.LogInformation($"Visiting {node.Repository}@{node.Commit}");

            List<DependencyDetail> dependencies;
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

                dependencies = (await GetDependenciesAsync(
                    remoteFactory,
                    remote,
                    logger,
                    options.GitExecutable,
                    node.Repository,
                    node.Commit,
                    options.IncludeToolset,
                    remotesList,
                    reposFolder,
                    testPath))?.ToList();
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

                    if (options.LookupBuilds)
                    {
                        if (!buildLookups.ContainsKey($"{dependency.RepoUri}@{dependency.Commit}"))
                        {
                            buildLookups.Add($"{dependency.RepoUri}@{dependency.Commit}",
                                (await barClient.GetBuildsAsync(dependency.RepoUri, dependency.Commit))
                                .ToList());
                        }
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
                    if (incoherentDependenciesCache.TryGetValue(dependency.Name,
                            out DependencyDetail existingDependency))
                    {
                        incoherentDependencies.Add(existingDependency);
                        incoherentDependencies.Add(dependency);
                    }
                    else
                    {
                        incoherentDependenciesCache.Add(dependency.Name, dependency);
                    }

                    // We may have visited this node before.  If so, add it as a child and avoid additional walks.
                    // Update the list of contributing builds.
                    if (nodeCache.TryGetValue($"{dependency.RepoUri}@{dependency.Commit}",
                            out DependencyGraphNode existingNode))
                    {
                        logger.LogInformation(
                            $"Node {dependency.RepoUri}@{dependency.Commit} has already been created, adding as child");
                        node.AddChild(existingNode, dependency);
                        continue;
                    }

                    // Otherwise, create a new node for this dependency.
                    var newNode = new DependencyGraphNode(
                        dependency.RepoUri,
                        dependency.Commit,
                        null,
                        node.VisitedNodes,
                        null);

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
                }
            }
        }

        if (options.LookupBuilds)
        {
            allContributingBuilds = ComputeContributingBuilds(
                buildLookups,
                nodeCache.Values,
                logger);
        }

        switch (options.NodeDiff)
        {
            case NodeDiff.None:
                // Nothing
                break;
            case NodeDiff.LatestInGraph:
                await DoLatestInGraphNodeDiffAsync(remoteFactory, logger, nodeCache, visitedRepoUriNodes);
                break;
            case NodeDiff.LatestInChannel:
                await DoLatestInChannelGraphNodeDiffAsync(remoteFactory, barClient, logger, nodeCache);
                break;
        }

        return new DependencyGraph(rootGraphNode,
            uniqueDependencyDetails,
            incoherentDependencies,
            nodeCache.Values,
            incoherentNodes,
            allContributingBuilds,
            cycles);
    }

    /// <summary>
    /// Compute which builds contribute to each node in the graph, as well as the overall graph
    /// </summary>
    /// <param name="buildLookups">Set of tasks that are looking up builds</param>
    /// <param name="allNodes">All nodes in the graph</param>
    /// <param name="logger">Logger</param>
    /// <returns></returns>
    private static List<Build> ComputeContributingBuilds(
        Dictionary<string, List<Build>> buildLookups,
        IEnumerable<DependencyGraphNode> allNodes,
        ILogger logger)
    {
        logger.LogInformation("Computing contributing builds");

        var allContributingBuilds = new List<Build>();

        foreach (DependencyGraphNode node in allNodes)
        {
            node.ContributingBuilds = new HashSet<Build>(new BuildComparer());
            IEnumerable<Build> potentiallyContributingBuilds = buildLookups[$"{node.Repository}@{node.Commit}"];

            // Filter down. The parent nodes of this node may have specific dependency versions that narrow down
            // which potential builds this could be.  For instance, if sha A was built twice, producing asset B.1 and B.2,
            // we wouldn't know which by a simple query. But we can narrow the potential
            // builds by any of those that produced assets that match any parent's dependency name+version
            foreach (Build potentialContributingBuild in potentiallyContributingBuilds)
            {
                if (BuildContributesToNode(node, potentialContributingBuild))
                {
                    allContributingBuilds.Add(potentialContributingBuild);
                    node.ContributingBuilds.Add(potentialContributingBuild);
                }
            }
        }

        logger.LogInformation("Done computing contributing builds");

        return allContributingBuilds;
    }

    /// <summary>
    /// Determines whether a build contributes to a given node by looking at the parents'
    /// input dependencies. If there are no parents, then we assume that the build could contribute. This
    /// would happen for the root node.
    /// </summary>
    /// <param name="node">Node</param>
    /// <param name="potentialContributingBuild">Potentially contributing build</param>
    /// <returns>True if the build contributes, false otherwise.</returns>
    private static bool BuildContributesToNode(DependencyGraphNode node, Build potentialContributingBuild)
    {
        if (!node.Parents.Any())
        {
            return true;
        }

        var assetEqualityComparer = new AssetComparer();
        foreach (DependencyGraphNode parentNode in node.Parents)
        {
            foreach (var dependency in parentNode.Dependencies)
            {
                if (dependency.Commit == node.Commit &&
                    dependency.RepoUri == node.Repository &&
                    potentialContributingBuild.Assets.Any(a => AssetComparer.Equals(a, dependency)))
                {
                    return true;
                }
            }
        }
        return false;
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
        var allCyclesRootedAtNode = new List<LinkedList<DependencyGraphNode>>();

        // Find all parents who have a path to the root node.  This set might also
        // be the root node, since the root node has itself marked in VisitedNodes.
        // After reaching the root along all paths, this set will be empty
        var parentsInCycles = currentNode.Parents.Where(p => p.VisitedNodes.Contains(repoCycleRoot)).ToList();

        if (parentsInCycles.Any())
        {
            // Recurse into each parent, combining the returned cycles together and
            // appending on the current node.
            foreach (DependencyGraphNode parent in parentsInCycles)
            {
                List<LinkedList<DependencyGraphNode>> cyclesRootedAtParentNode = ComputeCyclePaths(parent, repoCycleRoot);
                foreach (LinkedList<DependencyGraphNode> cycleRootedAtNode in cyclesRootedAtParentNode)
                {
                    cycleRootedAtNode.AddLast(currentNode);
                    allCyclesRootedAtNode.Add(cycleRootedAtNode);
                }
            }
        }
        else
        {
            // Create a segment containing just this node and return that
            var newCycleSegment = new LinkedList<DependencyGraphNode>();
            allCyclesRootedAtNode.Add(newCycleSegment);
            newCycleSegment.AddFirst(currentNode);
        }

        return allCyclesRootedAtNode;
    }

    private static async Task<string> GetRepoPathAsync(
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
            _remotesMapping ??= CreateRemotesMapping(remotesMap);

            if (!_remotesMapping.ContainsKey(repoPath))
            {
                throw new DarcException($"A key matching '{repoUri}' was not " +
                                        $"found in the mapping. Please make sure to include it...");
            }

            repoPath = _remotesMapping[repoPath];
        }
        else
        {
            string folder;
            if (!string.IsNullOrEmpty(reposFolder))
            {
                folder = reposFolder;
            }
            else
            {
                // If a repo folder or a mapping was not set we use the current parent's 
                // parent folder.
                var gitClient = new LocalLibGit2Client(
                    new RemoteTokenProvider((string)null, null),
                    new NoTelemetryRecorder(),
                    new ProcessManager(logger, gitExecutable),
                    new FileSystem(),
                    logger);

                var parent = await gitClient.GetRootDirAsync();
                folder = Directory.GetParent(parent).FullName;
            }

            repoPath = await GetRepoPathFromFolderAsync(gitExecutable, folder, commit, logger);

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
                    var local = new Local(new RemoteTokenProvider(), logger, testPath);
                    dependencies = await local.GetDependenciesAsync();
                }
            }
            else if (remote)
            {
                IRemote remoteClient = await remoteFactory.CreateRemoteAsync(repoUri);
                dependencies = await remoteClient.GetDependenciesAsync(repoUri, commit);
            }
            else
            {
                var repoPath = await GetRepoPathAsync(repoUri, commit, remotesMap, reposFolder, logger, gitExecutable);

                if (!string.IsNullOrEmpty(repoPath))
                {
                    var local = new Local(new RemoteTokenProvider(), logger);
                    var fileContents = await GitShowAsync(
                        gitExecutable,
                        repoPath,
                        commit,
                        VersionFiles.VersionDetailsXml,
                        logger);
                    dependencies = local.GetDependenciesFromFileContents(fileContents);
                }
            }

            if (!includeToolset && dependencies != null)
            {
                dependencies = dependencies.Where(dependency => dependency.Type != DependencyType.Toolset);
            }
            return dependencies;
        }
        catch (GithubApplicationInstallationException gexc)
        {
            // This means the Maestro APP was not installed in the repo's org. Just keep going.
            logger.LogWarning($"Failed to retrieve dependency information from {repoUri}@{commit}. Error: {gexc.Message}");
            return null;
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
        var remotesMapping = new Dictionary<string, string>();

        foreach (var remotes in remotesMap)
        {
            var keyValuePairs = remotes.Split(';');

            foreach (var keyValue in keyValuePairs)
            {
                var kv = keyValue.Split(',');
                remotesMapping.Add(kv[0], kv[1]);
            }
        }

        return remotesMapping;
    }

    private static async Task<string> GitShowAsync(string gitLocation, string repoFolderPath, string commit, string fileName, ILogger logger)
    {
        var processManager = new ProcessManager(logger, gitLocation);
        var result = await processManager.ExecuteGit(repoFolderPath, ["show", $"{commit}:{fileName}"]);

        if (!result.Succeeded || string.IsNullOrEmpty(result.StandardOutput))
        {
            throw new Exception($"Could not show the contents of '{fileName}' at '{commit}' in '{repoFolderPath}'...");
        }

        return result.StandardOutput;
    }


    /// <summary>
    /// For each child folder in the provided "source" folder we check for the existance of a given commit. Each folder in "source"
    /// represent a different repo.
    /// </summary>
    private static async Task<string> GetRepoPathFromFolderAsync(string gitLocation, string sourceFolder, string commit, ILogger logger)
    {
        var processManager = new ProcessManager(logger, gitLocation);

        foreach (var directory in Directory.GetDirectories(sourceFolder))
        {
            var result = await processManager.Execute(gitLocation, new[] { "branch", "--contains", commit }, workingDir: directory);
            if (!string.IsNullOrEmpty(result.StandardOutput))
            {
                return directory;
            }
        }

        return null;
    }
}

public class LooseDependencyDetailComparer : IEqualityComparer<DependencyDetail>
{
    public bool Equals(DependencyDetail x, DependencyDetail y)
    {
        return x.Commit == y.Commit &&
               x.Name == y.Name &&
               x.Version == y.Version;
    }

    public int GetHashCode(DependencyDetail obj)
    {
        return (obj.Commit,
            obj.Name,
            obj.Version).GetHashCode();
    }
}
