// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class GraphCloneClient
    {
        private const string GitDirRedirectPrefix = "gitdir: ";

        public string GitDir { get; set; }

        public ILogger Logger { get; set; }
        public IRemoteFactory RemoteFactory { get; set; }

        public bool IgnoreNonGitHub { get; set; } = true;

        public async Task<SourceBuildGraph> GetGraphAsync(
            IEnumerable<SourceBuildIdentity> rootDependencies,
            IEnumerable<string> ignoredRepos,
            bool includeToolset,
            bool forceCoherence,
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
                                if (IgnoreNonGitHub &&
                                    Uri.TryCreate(repo.RepoUri, UriKind.Absolute, out Uri parsedUri) &&
                                    parsedUri.Host != "github.com")
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
                                .Select(d => new SourceBuildIdentity(d.RepoUri, d.Commit))
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

        public async Task CreateWorkTreesAsync(SourceBuildGraph graph, string reposFolder)
        {
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

        private static async Task SetupMasterCopyAsync(IRemoteFactory remoteFactory, string repoUrl, string masterGitRepoPath, string masterRepoGitDirPath, ILogger log)
        {
            if (masterRepoGitDirPath != null)
            {
                await HandleMasterCopyWithGitDirPath(remoteFactory, repoUrl, masterGitRepoPath, masterRepoGitDirPath, log);
            }
            else
            {
                await HandleMasterCopyWithDefaultGitDir(remoteFactory, repoUrl, masterGitRepoPath, masterRepoGitDirPath, log);
            }
            Local local = new Local(log, masterGitRepoPath);
            local.AddRemoteIfMissing(masterGitRepoPath, repoUrl);
        }

        private static async Task HandleMasterCopyWithDefaultGitDir(IRemoteFactory remoteFactory, string repoUrl, string masterGitRepoPath, string masterRepoGitDirPath, ILogger log)
        {
            log.LogDebug($"Starting master copy for {repoUrl} in {masterGitRepoPath} with default .gitdir");

            // The master folder doesn't exist.  Just clone and set everything up for the first time.
            if (!Directory.Exists(masterGitRepoPath))
            {
                log.LogInformation($"Cloning master copy of {repoUrl} into {masterGitRepoPath}");
                IRemote repoRemote = await remoteFactory.GetRemoteAsync(repoUrl, log);
                repoRemote.Clone(repoUrl, null, masterGitRepoPath, masterRepoGitDirPath);
            }
            // The master folder already exists.  We are probably resuming with a different --git-dir-parent setting, or the .gitdir parent was cleaned.
            else
            {
                log.LogDebug($"Checking for existing .gitdir in {masterGitRepoPath}");
                string masterRepoPossibleGitDirPath = Path.Combine(masterGitRepoPath, ".git");
                // This repo is not in good shape for us.  It needs to be deleted and recreated.
                if (!Directory.Exists(masterRepoPossibleGitDirPath))
                {
                    throw new InvalidOperationException($"Repo {masterGitRepoPath} does not have a .git folder, but no git-dir-parent was specified.  Please fix the repo or specify a git-dir-parent.");
                }
            }
        }

        private static async Task HandleMasterCopyWithGitDirPath(IRemoteFactory remoteFactory, string repoUrl, string masterGitRepoPath, string masterRepoGitDirPath, ILogger log)
        {
            string gitDirRedirect = GetGitDirRedirectString(masterRepoGitDirPath);
            log.LogDebug($"Starting master copy for {repoUrl} in {masterGitRepoPath} with .gitdir {masterRepoGitDirPath}");

            // the .gitdir exists already.  We are resuming with the same --git-dir-parent setting.
            if (Directory.Exists(masterRepoGitDirPath))
            {
                HandleMasterCopyWithExistingGitDir(masterGitRepoPath, masterRepoGitDirPath, log, gitDirRedirect);
            }
            // The .gitdir does not exist.  This could be a new clone or resuming with a different --git-dir-parent setting.
            else
            {
                await HandleMasterCopyAndCreateGitDir(remoteFactory, repoUrl, masterGitRepoPath, masterRepoGitDirPath, gitDirRedirect, log);
            }
        }

        private static async Task HandleMasterCopyAndCreateGitDir(IRemoteFactory remoteFactory, string repoUrl, string masterGitRepoPath, string masterRepoGitDirPath, string gitDirRedirect, ILogger log)
        {
            log.LogDebug($"Master .gitdir {masterRepoGitDirPath} does not exist");

            // The master folder also doesn't exist.  Just clone and set everything up for the first time.
            if (!Directory.Exists(masterGitRepoPath))
            {
                log.LogInformation($"Cloning master copy of {repoUrl} into {masterGitRepoPath} with .gitdir path {masterRepoGitDirPath}");
                IRemote repoRemote = await remoteFactory.GetRemoteAsync(repoUrl, log);
                repoRemote.Clone(repoUrl, null, masterGitRepoPath, masterRepoGitDirPath);
            }
            // The master folder already exists.  We are probably resuming with a different --git-dir-parent setting, or the .gitdir parent was cleaned.
            else
            {
                string masterRepoPossibleGitDirPath = Path.Combine(masterGitRepoPath, ".git");
                // The master folder has a full .gitdir.  Relocate it to the .gitdir parent directory and update to redirect to that.
                if (Directory.Exists(masterRepoPossibleGitDirPath))
                {
                    log.LogDebug($".gitdir {masterRepoPossibleGitDirPath} exists in {masterGitRepoPath}");

                    // Check if the .gitdir is already where we expect it to be first.
                    if (Path.GetFullPath(masterRepoPossibleGitDirPath) != Path.GetFullPath(masterRepoGitDirPath))
                    {
                        log.LogDebug($"Moving .gitdir {masterRepoPossibleGitDirPath} to expected location {masterRepoGitDirPath}");
                        Directory.Move(masterRepoPossibleGitDirPath, masterRepoGitDirPath);
                        File.WriteAllText(masterRepoPossibleGitDirPath, gitDirRedirect);
                    }
                }
                // The master folder has a .gitdir redirect.  Relocate its .gitdir to where we expect and update the redirect.
                else if (File.Exists(masterRepoPossibleGitDirPath))
                {
                    log.LogDebug($"Master repo {masterGitRepoPath} has a .gitdir redirect");

                    string relocatedGitDirPath = File.ReadAllText(masterRepoPossibleGitDirPath).Substring(GitDirRedirectPrefix.Length);
                    if (Path.GetFullPath(relocatedGitDirPath) != Path.GetFullPath(masterRepoGitDirPath))
                    {
                        log.LogDebug($"Existing .gitdir redirect of {relocatedGitDirPath} does not match expected {masterRepoGitDirPath}, moving .gitdir and updating redirect");
                        Directory.Move(relocatedGitDirPath, masterRepoGitDirPath);
                        File.WriteAllText(masterRepoPossibleGitDirPath, gitDirRedirect);
                    }
                }
                // This repo is orphaned.  Since it's supposed to be our master copy, adopt it.
                else
                {
                    log.LogDebug($"Master repo {masterGitRepoPath} is orphaned, adding .gitdir redirect");
                    File.WriteAllText(masterRepoPossibleGitDirPath, gitDirRedirect);
                }
            }
        }

        private static void HandleMasterCopyWithExistingGitDir(string masterGitRepoPath, string masterRepoGitDirPath, ILogger log, string gitDirRedirect)
        {
            log.LogDebug($"Master .gitdir {masterRepoGitDirPath} exists");

            // the master folder doesn't exist yet.  Create it.
            if (!Directory.Exists(masterGitRepoPath))
            {
                log.LogDebug($"Master .gitdir exists and master folder {masterGitRepoPath} does not.  Creating master folder.");
                Directory.CreateDirectory(masterGitRepoPath);
                File.WriteAllText(Path.Combine(masterGitRepoPath, ".git"), gitDirRedirect);
                Local masterLocal = new Local(log, masterGitRepoPath);
                log.LogDebug($"Checking out default commit in {masterGitRepoPath}");
                masterLocal.Checkout(null, true);
            }
            // The master folder already exists.  Redirect it to the .gitdir we expect.
            else
            {
                log.LogDebug($"Master .gitdir exists and master folder {masterGitRepoPath} also exists.  Redirecting master folder.");

                string masterRepoPossibleGitDirPath = Path.Combine(masterGitRepoPath, ".git");
                if (Directory.Exists(masterRepoPossibleGitDirPath))
                {
                    Directory.Delete(masterRepoPossibleGitDirPath);
                }
                if (File.Exists(masterRepoPossibleGitDirPath))
                {
                    File.Delete(masterRepoPossibleGitDirPath);
                }
                File.WriteAllText(masterRepoPossibleGitDirPath, gitDirRedirect);
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

        private static string GetDefaultMasterGitDirPath(string reposFolder, string repoUri)
        {
            if (repoUri.EndsWith(".git"))
            {
                repoUri = repoUri.Substring(0, repoUri.Length - ".git".Length);
            }

            return Path.Combine(reposFolder, $"{repoUri.Substring(repoUri.LastIndexOf("/") + 1)}", ".git");
        }

        private static string GetMasterGitRepoPath(string reposFolder, string repoUri)
        {
            if (repoUri.EndsWith(".git"))
            {
                repoUri = repoUri.Substring(0, repoUri.Length - ".git".Length);
            }
            return Path.Combine(reposFolder, $"{repoUri.Substring(repoUri.LastIndexOf("/") + 1)}");
        }

        private static string GetGitDirRedirectString(string gitDir)
        {
            return $"{GitDirRedirectPrefix}{gitDir}";
        }
    }
}
