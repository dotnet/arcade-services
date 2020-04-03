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
using Microsoft.DotNet.DarcLib.Models.Darc;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class GraphCloneClient
    {
        public string GitDir { get; set; }

        public ILogger Logger { get; set; }
        public IRemoteFactory RemoteFactory { get; set; }

        public bool IgnoreNonGitHub { get; set; } = true;

        private Dictionary<string, Task> _cloningTasks = new Dictionary<string, Task>();
        private SemaphoreSlim _cloningTasksLock = new SemaphoreSlim(1);

        public async Task<SourceBuildGraph> GetGraphAsync(
            IEnumerable<SourceBuildIdentity> rootDependencies,
            IEnumerable<string> ignoredRepos,
            bool includeToolset,
            uint cloneDepth)
        {
            var nextLevelDependencies = rootDependencies
                .GroupBy(d => d, SourceBuildIdentity.CaseInsensitiveComparer)
                .Select(g => HighestVersionedDistinctIdentity(g))
                .ToArray();

            var allUpstreams = new Dictionary<SourceBuildIdentity, SourceBuildIdentity[]>(SourceBuildIdentity.CaseInsensitiveComparer);

            while (nextLevelDependencies.Any())
            {
                // Do one level of clones at a time.
                var discoveredUpstreamsRaw =
                    await Task.WhenAll(nextLevelDependencies.Select(async repo =>
                    {
                        if (IsRepoNonGitHubAndIgnored(repo))
                        {
                            Logger.LogWarning($"Skipping non-GitHub repo {repo.RepoUri}");
                            return (repo, Array.Empty<SourceBuildIdentity>());
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

                            var overrides = DarcCloneOverrideDetail.ParseAll(file.DocumentElement);

                            Logger.LogDebug($"Got {deps.Count()} dependencies.");

                            if (!includeToolset)
                            {
                                Logger.LogInformation($"Removing toolset dependencies...");
                                deps = deps.Where(dependency => dependency.Type != DependencyType.Toolset);
                                Logger.LogDebug($"Filtered toolset dependencies. Now {deps.Count()} dependencies");
                            }

                            var upstreamDependencies = deps
                                .Select(d => new SourceBuildIdentity(d.RepoUri, d.Commit, d))
                                .GroupBy(d => d, SourceBuildIdentity.CaseInsensitiveComparer)
                                .Select(g => HighestVersionedDistinctIdentity(g))
                                .ToArray();

                            foreach (var newIdentity in upstreamDependencies)
                            {
                                Logger.LogDebug($"Adding new dependency {newIdentity}");
                            }

                            Logger.LogDebug($"done looking for dependencies in {bareRepoDir} at {repo.Commit}");

                            return (repo, upstreamDependencies);
                        }
                        catch (DependencyFileNotFoundException ex)
                        {
                            Logger.LogWarning(
                                $"Repo {bareRepoDir} appears to have no " +
                                $"'/eng/Version.Details.xml' file at commit {repo.Commit}. " +
                                $"Dependency chain is broken here. " + Environment.NewLine +
                                $"(Inner exception: {ex})");

                            return (repo, Array.Empty<SourceBuildIdentity>());
                        }
                    }));

                // Exit early (before evaluating dependencies) if we have hit clone depth.
                if (cloneDepth == 0)
                {
                    Logger.LogInformation($"Reached clone depth limit, aborting with {nextLevelDependencies.Length} dependencies remaining");

                    foreach (var d in nextLevelDependencies)
                    {
                        Logger.LogDebug($"Abandoning dependency {d}");
                        // Ensure the just-evaluated nodes end up in the graph, even though we're
                        // abandoning their upstreams.
                        allUpstreams[d] = Array.Empty<SourceBuildIdentity>();
                    }

                    break;
                }

                cloneDepth--;
                Logger.LogDebug($"Clone depth remaining: {cloneDepth}");

                Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> discoveredUpstreams =
                    discoveredUpstreamsRaw.ToDictionary(
                        u => u.repo,
                        u => u.Item2,
                        SourceBuildIdentity.CaseInsensitiveComparer);

                // Create a temp graph that includes all potential nodes to evaluate next. We use
                // this graph to evaluate whether to evaluate each node.
                var graph = SourceBuildGraph.Create(
                    allUpstreams
                        .Concat(discoveredUpstreams)
                        .ToDictionary(
                            p => p.Key,
                            p => p.Value));

                bool ShouldSearchRepo(SourceBuildIdentity upstream, SourceBuildIdentity source)
                {
                    if (allUpstreams.ContainsKey(upstream) ||
                        discoveredUpstreams.ContainsKey(upstream))
                    {
                        return false;
                    }
                    // Scan this upstream in the next pass.
                    return true;
                }

                bool ShouldIncludeDependency(SourceBuildIdentity upstream, SourceBuildIdentity source)
                {
                    // Remove self-dependency. E.g. arcade depends on previous versions of itself to
                    // build, so this tends to go on essentially forever.
                    if (string.Equals(upstream.RepoUri, source.RepoUri, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.LogDebug($"Skipping self-dependency in {source.RepoUri} ({source.Commit} => {upstream.Commit})");
                        return false;
                    }
                    // Remove circular dependencies that have different hashes. That is, detect
                    // circular-by-name-only dependencies.
                    // e.g. DotNet-Trusted -> core-setup -> DotNet-Trusted -> ...
                    // We are working our way upstream, so this check walks all downstreams we've
                    // seen so far to see if any have this potential repo name. (We can't simply
                    // check if we've seen the repo name before: other branches may have the same
                    // repo name dependency but not as part of a circular dependency.)
                    var allDownstreams = graph.GetAllDownstreams(source).ToArray();
                    if (allDownstreams.Any(
                        d => string.Equals(d.RepoUri, source.RepoUri, StringComparison.OrdinalIgnoreCase)))
                    {
                        Logger.LogDebug(
                            $"Skipping already-seen circular dependency from {source} to {upstream}\n" +
                            string.Join(" -> ", allDownstreams.Select(d => d.ToString()))
                            );
                        return false;
                    }
                    // Remove repos specifically ignored by the caller.
                    if (ignoredRepos.Any(r => r.Equals(upstream.RepoUri, StringComparison.OrdinalIgnoreCase)))
                    {
                        Logger.LogDebug($"Skipping ignored repo {upstream}");
                        return false;
                    }
                    // Remove repos with invalid dependency info: missing commit.
                    if (string.IsNullOrWhiteSpace(upstream.Commit))
                    {
                        Logger.LogWarning($"Skipping dependency from {source} to {upstream.RepoUri}: Missing commit.");
                        return false;
                    }

                    return true;
                }

                var upstreamAssocToConsider = discoveredUpstreams
                    .SelectMany(
                        repoToUpstream => repoToUpstream.Value
                            .Where(upstream => ShouldIncludeDependency(upstream, repoToUpstream.Key))
                            .Select(upstream => new { sourceRepo = repoToUpstream.Key, upstream }))
                    .ToArray();

                // Add only the upstreams that we're going to evaluate to the accumulated dependency
                // map. Don't include entries that were discarded.
                foreach (var entry in upstreamAssocToConsider.GroupBy(m => m.sourceRepo, m => m.upstream))
                {
                    allUpstreams.Add(entry.Key, entry.ToArray());
                }

                nextLevelDependencies = upstreamAssocToConsider
                    .Where(mapping => ShouldSearchRepo(mapping.upstream, mapping.sourceRepo))
                    .Select(mapping => mapping.upstream)
                    .Distinct(SourceBuildIdentity.CaseInsensitiveComparer)
                    .ToArray();

                Logger.LogDebug($"Dependencies remaining: {nextLevelDependencies.Length}");
            }

            return SourceBuildGraph.Create(allUpstreams);
        }

        public async Task CreateWorktreesAsync(SourceBuildGraph graph, string reposFolder)
        {
            var nodesWithDistinctWorktree = graph.Nodes
                .Select(repo => new { repo, path = GetWorktreePath(reposFolder, repo) })
                .GroupBy(r => r.path)
                .Select(g => g.First().repo)
                .OrderBy(r => r.ToString())
                .ToArray();

            await Task.WhenAll(nodesWithDistinctWorktree.Select(async repo =>
            {
                if (IsRepoNonGitHubAndIgnored(repo))
                {
                    Logger.LogWarning($"Skipping non-GitHub repo {repo.RepoUri}");
                    return;
                }

                if (string.IsNullOrEmpty(repo.Commit))
                {
                    Logger.LogWarning($"Skipping repo with no commit hash: {repo}");
                    return;
                }

                string bareRepoDir = await GetInitializedBareRepoDirAsync(repo);
                string path = GetWorktreePath(reposFolder, repo);
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
                        repo.Commit,
                        Path.GetFileName(path) + DateTime.UtcNow.ToString("s").Replace(":", "."),
                        inProgressPath,
                        false);

                    Directory.Move(inProgressPath, path);

                    Logger.LogDebug($"Completed adding worktree: {path}");
                });
            }));
        }

        /// <summary>
        /// Create an artificially coherent graph: only keep one commit of each repo by name. For
        /// each identity node, the latest version is kept and dependenices on all versions are
        /// redirected to the kept version.
        ///
        /// If a node has no version information (no DependencyDetail) we assume it is the latest.
        /// This should only be the case when the user manually passes in a url and commit hash. If
        /// multiple nodes with the same name lack version information, throws an exception.
        /// </summary>
        public SourceBuildGraph CreateArtificiallyCoherentGraph(
            SourceBuildGraph source,
            Func<SourceBuildIdentity, DateTimeOffset?> getCommitDate = null)
        {
            // Not good enough. We need to be able to override this graph anyway, probably... giving
            // it direct guidance is likely good to make this comprehensible anyway and not rely on
            // heuristics!

            // Map old node => new node.
            Dictionary<SourceBuildIdentity, SourceBuildIdentity> newNodes = source.Nodes
                .GroupBy(n => n, SourceBuildIdentity.RepoNameOnlyComparer)
                .Select(group => HighestVersionedDistinctIdentity(group, getCommitDate))
                .ToDictionary(
                    n => n,
                    n => n,
                    SourceBuildIdentity.RepoNameOnlyComparer);

            Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> newUpstreamMap = source.Upstreams
                .GroupBy(
                    pair => newNodes[pair.Key],
                    // Transform all upstream nodes into the merged node, and dedup.
                    pair => pair.Value.Select(u => newNodes[u]).Distinct().ToArray())
                .ToDictionary(
                    group => group.Key,
                    // Combine all upstream lists for this merged node, and dedup.
                    group => group.SelectMany(upstreams => upstreams).Distinct().ToArray());

            return SourceBuildGraph.Create(newUpstreamMap);
        }

        private SourceBuildIdentity HighestVersionedDistinctIdentity(
            IGrouping<SourceBuildIdentity, SourceBuildIdentity> group,
            Func<SourceBuildIdentity, DateTimeOffset?> getCommitDate = null)
        {
            getCommitDate = getCommitDate ?? GetCachedCommitDateFunc();

            // If the repo has no source, it's likely a darc clone argument, and takes priority.
            if (group.SingleOrDefault(repo => repo.Source == null) is SourceBuildIdentity result)
            {
                return result;
            }

            return group
                // Use asset version as primary order. Pick the highest.
                .OrderByDescending(repo => NuGetVersion.Parse(repo.Source.Version))
                // Break ties using commit date. If the commit doesn't exist to check, this means
                // the remote doesn't have it: assume other choices are better.
                .ThenByDescending(repo => getCommitDate(repo) ?? DateTimeOffset.MinValue)
                .First();
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
