// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Models;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class GraphCloneClient
    {
        public string GitDir { get; set; }

        public ILogger Logger { get; set; }
        public IRemoteFactory RemoteFactory { get; set; }

        public bool IgnoreNonGitHub { get; set; } = true;

        public async Task<SourceBuildGraph> GetGraphAsync(
            IEnumerable<SourceBuildIdentity> rootDependencies,
            IEnumerable<string> ignoredRepos,
            bool includeToolset,
            uint cloneDepth)
        {
            var nextLevelDependencies = rootDependencies
                .Distinct(SourceBuildIdentity.CaseInsensitiveComparer)
                .ToArray();

            var allUpstreams = new Dictionary<SourceBuildIdentity, SourceBuildIdentity[]>(SourceBuildIdentity.CaseInsensitiveComparer);

            var cloningTasks = new Dictionary<string, Task>();
            var cloningTasksLock = new SemaphoreSlim(1);

            while (nextLevelDependencies.Any())
            {
                // Do one level of clones at a time.
                var discoveredUpstreamsRaw =
                    await Task.WhenAll(nextLevelDependencies.Select(async repo =>
                    {
                        // The bare .git dir.
                        string masterRepoGitDirPath = GetMasterGitDirPath(repo.RepoUri);

                        // Create the bare repo clone.
                        if (!Directory.Exists(masterRepoGitDirPath))
                        {
                            try
                            {
                                if (IsRepoNonGitHubAndIgnored(repo))
                                {
                                    Logger.LogWarning($"Skipping non-GitHub repo {repo.RepoUri}");
                                    return (repo, Array.Empty<SourceBuildIdentity>());
                                }

                                IRemote repoRemote = await RemoteFactory.GetRemoteAsync(repo.RepoUri, Logger);

                                var cloneTask = await GetOrCreateCloneTaskAsync(
                                    cloningTasks,
                                    cloningTasksLock,
                                    masterRepoGitDirPath,
                                    () =>
                                    {
                                        repoRemote.Clone(repo.RepoUri, null, null, masterRepoGitDirPath);
                                    });

                                await cloneTask;
                            }
                            catch (Exception)
                            {
                                Logger.LogError($"Failed to create bare clone of '{repo.RepoUri}' at '{masterRepoGitDirPath}'");
                                throw;
                            }
                        }

                        Logger.LogDebug($"Starting to look for dependencies in {masterRepoGitDirPath}");
                        try
                        {
                            var local = new Local(Logger, masterRepoGitDirPath);

                            IEnumerable<DependencyDetail> deps =
                                await local.GetDependenciesAsync(branch: repo.Commit);

                            Logger.LogDebug($"Got {deps.Count()} dependencies.");

                            if (!includeToolset)
                            {
                                Logger.LogInformation($"Removing toolset dependencies...");
                                deps = deps.Where(dependency => dependency.Type != DependencyType.Toolset);
                                Logger.LogDebug($"Filtered toolset dependencies. Now {deps.Count()} dependencies");
                            }

                            var upstreamDependencies = deps
                                .Select(d => new SourceBuildIdentity(d.RepoUri, d.Commit, d))
                                .Distinct(SourceBuildIdentity.CaseInsensitiveComparer)
                                .ToArray();

                            foreach (var newIdentity in upstreamDependencies)
                            {
                                Logger.LogDebug($"Adding new dependency {newIdentity}");
                            }

                            Logger.LogDebug($"done looking for dependencies in {masterRepoGitDirPath} at {repo.Commit}");

                            return (repo, upstreamDependencies);
                        }
                        catch (DependencyFileNotFoundException ex)
                        {
                            Logger.LogWarning(
                                $"Repo {masterRepoGitDirPath} appears to have no " +
                                $"'/eng/Version.Details.xml' file at commit {repo.Commit}. " +
                                $"Dependency chain is broken here. " + Environment.NewLine +
                                $"(Inner exception: {ex})");

                            return (repo, Array.Empty<SourceBuildIdentity>());
                        }
                    }));

                Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> discoveredUpstreams =
                    discoveredUpstreamsRaw
                        .ToDictionary(u => u.repo, u => u.Item2, SourceBuildIdentity.CaseInsensitiveComparer);

                foreach (var entry in discoveredUpstreams)
                {
                    if (allUpstreams.ContainsKey(entry.Key))
                    {
                        Logger.LogError($"Upstream mapping already contains entry for {entry.Key}.");
                    }

                    allUpstreams.Add(entry.Key, entry.Value);
                }

                var graph = SourceBuildGraph.Create(allUpstreams);

                bool ShouldSearchRepo(SourceBuildIdentity upstream, SourceBuildIdentity source)
                {
                    // Remove repo we've already scanned before.
                    if (allUpstreams.ContainsKey(upstream))
                    {
                        return false;
                    }
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
                    if (graph.GetAllDownstreams(upstream).Any(
                        d => string.Equals(d.RepoUri, source.RepoUri, StringComparison.OrdinalIgnoreCase)))
                    {
                        Logger.LogDebug($"Skipping already-seen circular dependency from {source.RepoUri} to {upstream.RepoUri}");
                        return false;
                    }
                    // Remove repos specifically ignored by the caller.
                    if (ignoredRepos.Any(r => r.Equals(upstream.RepoUri, StringComparison.OrdinalIgnoreCase)))
                    {
                        Logger.LogDebug($"Skipping ignored repo {upstream.RepoUri} (at {upstream.Commit})");
                        return false;
                    }
                    // Remove repos with invalid dependency info: missing commit.
                    if (string.IsNullOrWhiteSpace(upstream.Commit))
                    {
                        Logger.LogWarning($"Skipping dependency from {source} to {upstream.RepoUri}: Missing commit.");
                        return false;
                    }

                    // Scan this upstream in the next pass.
                    return true;
                }

                nextLevelDependencies = discoveredUpstreams
                    .SelectMany(
                        repoToUpstream => repoToUpstream.Value
                            .Where(upstream => ShouldSearchRepo(upstream, repoToUpstream.Key)))
                    .Distinct(SourceBuildIdentity.CaseInsensitiveComparer)
                    .ToArray();

                if (cloneDepth == 0 && nextLevelDependencies.Any())
                {
                    Logger.LogInformation($"Reached clone depth limit, aborting with {nextLevelDependencies.Length} dependencies remaining");
                    foreach (var d in nextLevelDependencies)
                    {
                        Logger.LogDebug($"Abandoning dependency {d}");
                    }

                    break;
                }

                cloneDepth--;
                Logger.LogDebug($"Clone depth remaining: {cloneDepth}");
                Logger.LogDebug($"Dependencies remaining: {nextLevelDependencies.Length}");
            }

            return SourceBuildGraph.Create(allUpstreams);
        }

        public async Task CreateWorktreesAsync(SourceBuildGraph graph, string reposFolder)
        {
            await Task.WhenAll(graph.Nodes.Select(async repo =>
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

                string masterRepoGitDirPath = GetMasterGitDirPath(repo.RepoUri);
                string path = GetWorktreePath(reposFolder, repo);

                if (Directory.Exists(path))
                {
                    Logger.LogWarning($"Worktree already exists: '{path}'. Skipping.");
                    return;
                }

                Local local = new Local(Logger, masterRepoGitDirPath);

                Logger.LogInformation($"Creating worktree for {repo} at '{path}'...");

                await Task.Run(() =>
                {
                    local.AddWorktree(
                        repo.Commit,
                        Path.GetFileName(path) + DateTime.UtcNow.ToString("s").Replace(":", "."),
                        path,
                        false);
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
            Func<SourceBuildIdentity, DateTimeOffset> getCommitDate = null)
        {
            getCommitDate = getCommitDate ?? GetCachedCommitDateFunc();

            // Map old node => new node.
            Dictionary<SourceBuildIdentity, SourceBuildIdentity> newNodes = source.Nodes
                .GroupBy(n => n, SourceBuildIdentity.RepoNameOnlyComparer)
                .Select(group =>
                    group.SingleOrDefault(g => g.Source == null) ??
                    group.OrderByDescending(n => NuGetVersion.Parse(n.Source.Version))
                        .ThenByDescending(getCommitDate)
                        .First())
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

        private Func<SourceBuildIdentity, DateTimeOffset> GetCachedCommitDateFunc()
        {
            var gitClient = new LocalGitClient(null, Logger);
            var extraCommitData = new Dictionary<SourceBuildIdentity, DateTimeOffset>();

            return repo =>
            {
                if (IsRepoNonGitHubAndIgnored(repo) || string.IsNullOrEmpty(repo.Commit))
                {
                    return DateTimeOffset.MinValue;
                }

                if (extraCommitData.TryGetValue(repo, out var data))
                {
                    return data;
                }

                Commit commit = gitClient.GetCommit(GetMasterGitDirPath(repo.RepoUri), repo.Commit);
                return extraCommitData[repo] = commit.Committer.When;
            };
        }

        private bool IsRepoNonGitHubAndIgnored(SourceBuildIdentity repo)
        {
            return IgnoreNonGitHub &&
                Uri.TryCreate(repo.RepoUri, UriKind.Absolute, out Uri parsedUri) &&
                parsedUri.Host != "github.com";
        }

        private static async Task<Task> GetOrCreateCloneTaskAsync(
            Dictionary<string, Task> map,
            SemaphoreSlim mapLock,
            string path,
            Action cloneAction)
        {
            try
            {
                await mapLock.WaitAsync();

                return map.TryGetValue(path, out var task)
                    ? task
                    : map[path] = Task.Run(cloneAction);
            }
            finally
            {
                mapLock.Release();
            }
        }

        private string GetMasterGitDirPath(string repoUri)
        {
            if (string.IsNullOrEmpty(GitDir))
            {
                throw new ArgumentException(nameof(GitDir));
            }

            if (repoUri.EndsWith(".git"))
            {
                repoUri = repoUri.Substring(0, repoUri.Length - ".git".Length);
            }

            return Path.Combine(GitDir, $"{repoUri.Substring(repoUri.LastIndexOf("/") + 1)}.git");
        }

        private static string GetWorktreePath(string reposFolder, SourceBuildIdentity repo)
        {
            var uri = repo.RepoUri;

            if (uri.EndsWith(".git"))
            {
                uri = uri.Substring(0, uri.Length - ".git".Length);
            }

            var lastSegment = uri.Substring(uri.LastIndexOf("/", StringComparison.Ordinal) + 1);

            return Path.Combine(reposFolder, $"{lastSegment}.{repo.Commit.Substring(0, 8)}");
        }
    }
}
