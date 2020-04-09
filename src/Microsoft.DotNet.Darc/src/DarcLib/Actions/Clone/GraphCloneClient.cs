// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class GraphCloneClient
    {
        public string GitDir { get; set; }

        public ILogger Logger { get; set; }
        public IRemoteFactory RemoteFactory { get; set; }

        public bool IgnoreNonGitHub { get; set; } = true;

        /// <summary>
        /// Custom function to get commit date. If null, use a git client on the local clone.
        /// </summary>
        public Func<SourceBuildIdentity, DateTimeOffset?> GetCommitDate { get; set; }

        private Dictionary<string, Task> _cloningTasks = new Dictionary<string, Task>();
        private SemaphoreSlim _cloningTasksLock = new SemaphoreSlim(1);

        public async Task<SourceBuildGraph> GetGraphAsync(
            IEnumerable<SourceBuildIdentity> rootDependencies,
            IEnumerable<DarcCloneOverrideDetail> rootOverrides,
            IEnumerable<string> ignoredRepos,
            bool includeToolset,
            uint cloneDepth)
        {
            var nextLevelDependencies = rootDependencies
                .Distinct(SourceBuildIdentity.CaseInsensitiveComparer)
                .ToArray();

            var allNodes = new List<SourceBuildNode>();

            while (nextLevelDependencies.Any())
            {
                // Exit early (before evaluating dependencies) if we have hit clone depth.
                if (cloneDepth == 0)
                {
                    Logger.LogInformation($"Reached clone depth limit, aborting with {nextLevelDependencies.Length} dependencies remaining");

                    foreach (var d in nextLevelDependencies)
                    {
                        Logger.LogDebug($"Abandoning dependency {d}");
                        // Ensure the just-evaluated nodes end up in the graph, even though we're
                        // abandoning their upstreams.
                        allNodes.Add(new SourceBuildNode { Identity = d });
                    }

                    break;
                }

                // Do one level of clones at a time.
                var discoveredNodes =
                    await Task.WhenAll(nextLevelDependencies.Select(async repo =>
                    {
                        var result = new SourceBuildNode { Identity = repo };

                        if (IsRepoNonGitHubAndIgnored(repo))
                        {
                            Logger.LogWarning($"Skipping non-GitHub repo {repo.RepoUri}");
                            return result;
                        }

                        // The bare .git dir.
                        string bareRepoDir = await GetInitializedBareRepoDirAsync(repo);

                        Logger.LogDebug($"Starting to look for dependencies in {bareRepoDir}");
                        try
                        {
                            var local = new Local(Logger, bareRepoDir);

                            XmlDocument file = await local
                                .GetDependencyFileXmlContentAsync(repo.Commit);

                            IEnumerable<DependencyDetail> deps = DependencyDetail.ParseAll(file);

                            result.Overrides = DarcCloneOverrideDetail.ParseAll(file.DocumentElement);

                            Logger.LogDebug(
                                $"Got {deps.Count()} dependencies and " +
                                $"{result.Overrides.Count()} overrides.");

                            if (!includeToolset)
                            {
                                deps = deps.Where(dependency => dependency.Type != DependencyType.Toolset);
                                Logger.LogDebug($"Filtered toolset dependencies. Now {deps.Count()} dependencies");
                            }

                            result.UpstreamEdges = deps
                                .Select(d =>
                                {
                                    var identity = new SourceBuildIdentity
                                    {
                                        RepoUri = d.RepoUri,
                                        Commit = d.Commit,
                                    };

                                    bool? critical = null;

                                    foreach (var rootOverride in rootOverrides)
                                    {
                                        if (string.Equals(rootOverride.Repo, identity.RepoUri))
                                        {
                                            foreach (var find in rootOverride.FindDependencies)
                                            {
                                                critical = find?.ProductCritical ?? critical;
                                            }
                                        }
                                    }

                                    return new SourceBuildEdge
                                    {
                                        Upstream = identity,
                                        Downstream = repo,
                                        Source = d,
                                        ProductCritical = critical ?? false
                                    };
                                })
                                .ToArray();

                            Logger.LogDebug(
                                $"Adding {result.UpstreamEdges.Count()} new edges from {repo}\n" +
                                string.Join("\n", result.UpstreamEdges.Select(e => e.ToString()).Distinct()));

                            Logger.LogDebug($"done looking for dependencies in {bareRepoDir} at {repo.Commit}");
                        }
                        catch (DependencyFileNotFoundException ex)
                        {
                            Logger.LogWarning(
                                $"Repo {bareRepoDir} appears to have no " +
                                $"'/eng/Version.Details.xml' file at commit {repo.Commit}. " +
                                $"Dependency chain is broken here. " + Environment.NewLine +
                                $"(Inner exception: {ex})");
                        }

                        return result;
                    }));

                cloneDepth--;
                Logger.LogDebug($"Clone depth remaining: {cloneDepth}");

                // Create a temp graph that includes all potential nodes to evaluate next. We use
                // this graph to evaluate whether to evaluate each node.
                var graph = SourceBuildGraph.Create(
                    allNodes.Concat(discoveredNodes),
                    rootOverrides);

                SkipDependencyExplorationExplanation ReasonToSkipDependency(
                    SourceBuildIdentity upstream,
                    SourceBuildIdentity source)
                {
                    if (allNodes.Concat(discoveredNodes)
                        .Any(n => SourceBuildIdentity.CaseInsensitiveComparer.Equals(n.Identity, upstream)))
                    {
                        return new SkipDependencyExplorationExplanation
                        {
                            Reason = SkipDependencyExplorationReason.AlreadyVisited
                        };
                    }

                    // Remove self-dependency. E.g. arcade depends on previous versions of itself to
                    // build, so this tends to go on essentially forever.
                    if (string.Equals(upstream.RepoUri, source.RepoUri, StringComparison.OrdinalIgnoreCase))
                    {
                        return new SkipDependencyExplorationExplanation
                        {
                            Reason = SkipDependencyExplorationReason.SelfDependency,
                            Details = $"Skipping self-dependency in {source.RepoUri} ({source.Commit} => {upstream.Commit})"
                        };
                    }
                    // Remove circular dependencies that have different hashes. That is, detect
                    // circular-by-name-only dependencies.
                    // e.g. DotNet-Trusted -> core-setup -> DotNet-Trusted -> ...
                    // We are working our way upstream, so this check walks all downstreams we've
                    // seen so far to see if any have this potential repo name. (We can't simply
                    // check if we've seen the repo name before: other branches may have the same
                    // repo name dependency but not as part of a circular dependency.)
                    var allDownstreams = graph.GetAllDownstreams(upstream).ToArray();
                    if (allDownstreams.Any(
                        d => SourceBuildIdentity.RepoNameOnlyComparer.Equals(d.Identity, upstream)))
                    {
                        return new SkipDependencyExplorationExplanation
                        {
                            Reason = SkipDependencyExplorationReason.CircularWhenOnlyConsideringName,
                            Details =
                                $"Skipping already-seen circular dependency from {source} to {upstream}\n" +
                                string.Join(" -> ", allDownstreams.Select(d => d.ToString()))
                        };
                    }
                    // Remove repos specifically ignored by the caller.
                    if (ignoredRepos.Any(r => r.Equals(upstream.RepoUri, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new SkipDependencyExplorationExplanation
                        {
                            Reason = SkipDependencyExplorationReason.Ignored,
                            Details = $"Skipping ignored repo {upstream}"
                        };
                    }
                    // Remove repos with invalid dependency info: missing commit.
                    if (string.IsNullOrWhiteSpace(upstream.Commit))
                    {
                        return new SkipDependencyExplorationExplanation
                        {
                            Reason = SkipDependencyExplorationReason.DependencyDetailMissingCommit,
                            Details = $"Skipping dependency from {source} to {upstream.RepoUri}: Missing commit."
                        };
                    }

                    return null;
                }

                // Fill in each discovered node's reasons to skip dependencies (if exist).
                foreach (var node in discoveredNodes)
                {
                    foreach (var edge in node.UpstreamEdges.NullAsEmpty())
                    {
                        edge.SkippedReason = ReasonToSkipDependency(edge.Upstream, node.Identity);

                        if (edge.SkippedReason?.Reason != SkipDependencyExplorationReason.AlreadyVisited)
                        {
                            edge.FirstDiscoverer = true;
                        }
                    }
                }

                var upstreamEdgeToConsider = discoveredNodes
                    .SelectMany(
                        source => source.UpstreamEdges.NullAsEmpty()
                            .Select(
                                edge => new
                                {
                                    SkipReason = edge.SkippedReason,
                                    Source = source,
                                    Edge = edge
                                }))
                    .ToArray();

                allNodes.AddRange(discoveredNodes);

                nextLevelDependencies = upstreamEdgeToConsider
                    .Where(u => u.SkipReason == null)
                    .Select(u => u.Edge.Upstream)
                    .Distinct(SourceBuildIdentity.CaseInsensitiveComparer)
                    .ToArray();

                Logger.LogDebug($"Dependencies remaining: {nextLevelDependencies.Length}");
            }

            return SourceBuildGraph.Create(allNodes, rootOverrides);
        }

        public async Task CreateWorktreesAsync(SourceBuildGraph graph, string reposFolder)
        {
            var nodesWithDistinctWorktree = graph.Nodes
                .Select(repo => new { repo, path = GetWorktreePath(reposFolder, repo.Identity) })
                .GroupBy(r => r.path)
                .Select(g => g.First().repo)
                .OrderBy(r => r.ToString())
                .ToArray();

            await Task.WhenAll(nodesWithDistinctWorktree.Select(async repo =>
            {
                var identity = repo.Identity;
                if (IsRepoNonGitHubAndIgnored(identity))
                {
                    Logger.LogWarning($"Skipping non-GitHub repo {identity.RepoUri}");
                    return;
                }

                if (string.IsNullOrEmpty(identity.Commit))
                {
                    Logger.LogWarning($"Skipping repo with no commit hash: {repo}");
                    return;
                }

                string bareRepoDir = await GetInitializedBareRepoDirAsync(identity);
                string path = GetWorktreePath(reposFolder, identity);
                string inProgressPath = $"{path}~~~";

                if (Directory.Exists(path))
                {
                    Logger.LogWarning($"Worktree already exists: '{path}'. Skipping.");
                    return;
                }
                if (Directory.Exists(inProgressPath))
                {
                    Directory.Delete(inProgressPath, true);
                }

                Local local = new Local(Logger, bareRepoDir);

                Logger.LogInformation($"Creating worktree for {repo} at '{path}'...");

                await Task.Run(() =>
                {
                    Logger.LogDebug($"Starting adding worktree: {inProgressPath}");

                    local.AddWorktree(
                        identity.Commit,
                        Path.GetFileName(path) + DateTime.UtcNow.ToString("s").Replace(":", "."),
                        inProgressPath,
                        false);

                    Directory.Move(inProgressPath, path);

                    Logger.LogDebug($"Completed adding worktree: {path}");
                });
            }));
        }

        /// <summary>
        /// Create an artificially coherent graph: only keep one commit of each repo by name.
        /// </summary>
        public SourceBuildGraph CreateArtificiallyCoherentGraph(SourceBuildGraph source)
        {
            // Re-walk the source graph to form a graph based on product critical edges. We can't
            // simply take all critical edges, because there may be duplicates that exist in the
            // graph but aren't reachable. For example, repo A at commit 1 and commit 2 would have
            // similar dependencies, including criticality. However, A1 and A2 will be resolved so
            // only one is present in the graph. To determine which one, though, we need to know
            // which edges point to them, and which of those edges are reachable in the graph. To
            // simplify this, we visit one node at a time. Breadth-first, simply because it is
            // likely to resolve common .NET build graphs sooner.

            var toVisit = new Queue<SourceBuildNode>(source.GetRootNodes());
            var visited = new HashSet<SourceBuildIdentity>(SourceBuildIdentity.CaseInsensitiveComparer);

            var newGraph = SourceBuildGraph.Create(Enumerable.Empty<SourceBuildNode>());

            while (toVisit.Any())
            {
                SourceBuildNode next = toVisit.Dequeue();

                // Is this node a repo that already exists in the graph? If so, see if ours is a
                // better fit. Add the new node or remove the old and redo dependencies to match.

                // If this is a new repo, just add it.
                newGraph = SourceBuildGraph.Create(newGraph.Nodes.Concat(new[] { next }));
            }

            var edgesWithUpstreamNodeByName = source.AllEdges
                .GroupBy(edge => edge.Upstream, SourceBuildIdentity.RepoNameOnlyComparer)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToArray(),
                    SourceBuildIdentity.RepoNameOnlyComparer);

            // Pick which node to keep, and create mapping of old node identities into new nodes.
            var newMergedNodeData = source.Nodes
                .GroupBy(n => n.Identity, SourceBuildIdentity.RepoNameOnlyComparer)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        SourceBuildEdge pickedEdge;

                        if (edgesWithUpstreamNodeByName.TryGetValue(g.Key, out var edges))
                        {
                            if (edges.FirstOrDefault(e => e.ProductCritical) is SourceBuildEdge criticalEdge)
                            {
                                // If any edge that points at this RepoName is critical, pick that.
                                pickedEdge = criticalEdge;
                            }
                            else
                            {
                                // If no edges are critical, use heuristic.
                                pickedEdge = HighestVersionedDistinctIdentity(edges);
                            }
                        }
                        else
                        {
                            // If no edges exist...
                            return null;
                        }

                        SourceBuildNode upstreamNode = source.IdentityNodes[pickedEdge.Upstream];

                        return new SourceBuildNode
                        {
                            Identity = upstreamNode.Identity,
                            UpstreamEdges = upstreamNode.UpstreamEdges,
                        };
                    },
                    SourceBuildIdentity.RepoNameOnlyComparer);

            // Fix up upstreams. Now that we know the full set of merged nodes, repoint and dedup.
            foreach (var m in newMergedNodeData.Values)
            {
                if (m == null)
                    continue;
                m.UpstreamEdges = m.UpstreamEdges.NullAsEmpty()
                    .Select(edge => new SourceBuildEdge
                    {
                        Upstream = newMergedNodeData[edge.Upstream].Identity,
                        Downstream = newMergedNodeData[edge.Downstream].Identity,
                        FirstDiscoverer = edge.FirstDiscoverer,
                        SkippedReason = null,
                    })
                    .GroupBy(edge => edge.Upstream, SourceBuildIdentity.CaseInsensitiveComparer)
                    .Select(edge => edge.First())
                    .ToArray();
            }

            return SourceBuildGraph.Create(newMergedNodeData.Values, source.GlobalOverrides);
        }

        private SourceBuildEdge HighestVersionedDistinctIdentity(
            IEnumerable<SourceBuildEdge> edges)
        {
            var getCommitDate = GetCommitDate ?? GetCachedCommitDateFunc();

            // If an edge has no source, it's likely a darc clone argument, and takes priority.
            if (edges.FirstOrDefault(edge => edge.Source == null)
                is SourceBuildEdge result)
            {
                return result;
            }

            return edges
                .Select(edge => new
                {
                    edge,
                    version = NuGetVersion.Parse(edge.Source.Version)
                })
                // If there are multiple versions of the same commit, take the highest.
                // Otherwise, we'd check every single one with the later ThenByDescending.
                .GroupBy(d => d.edge.Upstream.Commit)
                .Select(g => g.OrderByDescending(d => d.version).First())
                // Use asset version as primary order. Pick the highest.
                .OrderByDescending(d => d.version)
                // Break ties using commit date. If the commit doesn't exist to check, this means
                // the remote doesn't have it: assume other choices are better.
                .ThenByDescending(d => getCommitDate(d.edge.Upstream) ?? DateTimeOffset.MinValue)
                .FirstOrDefault()
                ?.edge;
        }

        private Func<SourceBuildIdentity, DateTimeOffset?> GetCachedCommitDateFunc()
        {
            var gitClient = new LocalGitClient(null, Logger);
            var extraCommitData = new Dictionary<SourceBuildIdentity, DateTimeOffset?>();

            return repo =>
            {
                if (IsRepoNonGitHubAndIgnored(repo) || string.IsNullOrEmpty(repo.Commit))
                {
                    return null;
                }

                if (extraCommitData.TryGetValue(repo, out var data))
                {
                    return data;
                }

                try
                {
                    Commit commit = gitClient.GetCommit(GetBareRepoDir(repo.RepoUri), repo.Commit);
                    return extraCommitData[repo] = commit.Committer.When;
                }
                catch (CommitNotFoundException)
                {
                    return extraCommitData[repo] = null;
                }
            };
        }

        private bool IsRepoNonGitHubAndIgnored(SourceBuildIdentity repo)
        {
            return IgnoreNonGitHub &&
                Uri.TryCreate(repo.RepoUri, UriKind.Absolute, out Uri parsedUri) &&
                parsedUri.Host != "github.com";
        }

        private async Task<string> GetInitializedBareRepoDirAsync(SourceBuildIdentity repo)
        {
            string gitDir = GetBareRepoDir(repo.RepoUri);
            string inProgressGitDir = $"{gitDir}~~~";

            Task cloneTask;

            await _cloningTasksLock.WaitAsync();

            try
            {
                if (!_cloningTasks.TryGetValue(gitDir, out cloneTask))
                {
                    cloneTask = _cloningTasks[gitDir] = Task.Run(async () =>
                    {
                        try
                        {
                            if (Directory.Exists(gitDir))
                            {
                                // Ensure repo is up to date. This task runs once per lifetime of
                                // the client, on first use, so this is a nice place to do it.
                                Local local = new Local(Logger, gitDir);
                                local.Fetch();

                                return;
                            }
                            if (Directory.Exists(inProgressGitDir))
                            {
                                Logger.LogDebug($"Found existing in-progress dir to delete: {inProgressGitDir}");
                                try
                                {
                                    Directory.Delete(inProgressGitDir, true);
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    // Some files may be readonly and unable to be removed by
                                    // Directory.Delete. Try again after normalizing the attributes.
                                    // https://github.com/libgit2/libgit2sharp/issues/769
                                    GitFileManager.NormalizeAttributes(inProgressGitDir);
                                    Directory.Delete(inProgressGitDir, true);
                                }
                            }

                            IRemote repoRemote = await RemoteFactory.GetRemoteAsync(repo.RepoUri, Logger);
                            repoRemote.Clone(repo.RepoUri, null, null, inProgressGitDir);
                            Directory.Move(inProgressGitDir, gitDir);

                            Logger.LogDebug($"Completed bare clone into: {gitDir}");
                        }
                        catch (Exception)
                        {
                            Logger.LogError($"Failed to create bare clone of '{repo.RepoUri}' at '{gitDir}'");
                            throw;
                        }
                    });
                }
            }
            finally
            {
                _cloningTasksLock.Release();
            }

            await cloneTask;

            return gitDir;
        }

        private string GetBareRepoDir(string repoUri)
        {
            if (string.IsNullOrEmpty(GitDir))
            {
                throw new ArgumentException(nameof(GitDir));
            }

            if (repoUri.EndsWith(".git"))
            {
                repoUri = repoUri.Substring(0, repoUri.Length - ".git".Length);
            }

            var lastSegment = repoUri
                .Substring(repoUri.LastIndexOf("/", StringComparison.Ordinal) + 1)
                .ToLowerInvariant();

            return Path.Combine(GitDir, $"{lastSegment}.git");
        }

        private static string GetWorktreePath(string reposFolder, SourceBuildIdentity repo)
        {
            var uri = repo.RepoUri;

            if (uri.EndsWith(".git"))
            {
                uri = uri.Substring(0, uri.Length - ".git".Length);
            }

            var lastSegment = uri
                .Substring(uri.LastIndexOf("/", StringComparison.Ordinal) + 1)
                .ToLowerInvariant();

            return Path.Combine(reposFolder, $"{lastSegment}.{repo.ShortCommit}");
        }
    }
}
