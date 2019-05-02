// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    /// <summary>
    /// This operation has two modes:
    /// - In remote mode, it will clone a single remote repo, then recursively fetch all repos it depends on.
    /// - In local mode, it will take all the dependencies in the current repo's Version.Details.xml, clone them, and recursively fetch repos that they depend on.
    /// For performance reasons, we do some gymnastics so that we only ever have to actually clone a repo once.
    /// For user convenience reasons, the generally-intended mode of operation is to use a separate folder for .gitdirs, which allows a developer to do a "git clean" and not have to reclone all of the repos again for the next build.
    /// This is also useful for performance.  The general approach is:
    /// 1. Clone a "master" version of a repo.  LibGit2 does not currently support cloning with a separate .gitdir, so we then:
    /// 2. Move the .gitdir to the specified location.  If there is none specified, the .gitdir stays in the repo as usual.
    /// 3. Create a file (not directory) called ".git" and containing the new .gitdir that we just moved so Git can use the "master" folder properly as a repo.
    ///     Note: The "master" folder is also the only one that will end up being a real Git repo at the end of the process.
    /// 4. For each SHA that we want a copy of a repo at, create a new folder called repo.hash and add the same .gitdir redirect.
    /// 5. Checkout the SHA in that folder.  This causes a minor change in the "master" folder in that it changes the HEAD, but it will be otherwise unaffected.
    /// 6. Delete the .gitdir redirect file in the new repo.hash folder to orphan the repo.  This is done to avoid confusion about every repo.hash folder showing up with dirty files (since the repo thinks its HEAD is at a different SHA).
    /// Submodules add some complexity to this process.  Once a submodule has been initialized in the .gitdir, it will be initialized for all repos; however, the submodule will not have one of the .gitdir redirects.
    /// In this case, we go back to the "master" folder, which will have a .git/modules/subrepo directory, and redirect the submodule to this.  This means that if a submodule is included in multiple repos, we will have multiple copies of it, but this hasn't seemed to have a large perf impact.
    /// After cloning, we use local dependency graph discovery to add new repo-hash combinations to clone at.  This is done in waves to support a "depth limit" of followed repos.
    /// </summary>
    internal class CloneOperation : Operation
    {
        CloneCommandLineOptions _options;

        private const string GitDirRedirectPrefix = "gitdir: ";

        public CloneOperation(CloneCommandLineOptions options) : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            try
            {
                EnsureOptionsCompatibility(_options);
                // use a set to accumulate dependencies as we go
                HashSet<StrippedDependency> accumulatedDependencies = new HashSet<StrippedDependency>();
                // at the end of each depth level, these are added to the queue to clone
                Queue<StrippedDependency> dependenciesToClone = new Queue<StrippedDependency>();
                RemoteFactory remoteFactory = new RemoteFactory(_options);

                if (string.IsNullOrWhiteSpace(_options.RepoUri))
                {
                    Local local = new Local(Logger);
                    IEnumerable<DependencyDetail>  rootDependencies = await local.GetDependenciesAsync();
                    IEnumerable<StrippedDependency> stripped = rootDependencies.Select(d => StrippedDependency.GetDependency(d));
                    foreach (StrippedDependency d in stripped)
                    {
                        accumulatedDependencies.Add(d);
                    }
                    Logger.LogInformation($"Found {rootDependencies.Count()} local dependencies.  Starting deep clone...");
                }
                else
                {
                    // Start with the root repo we were asked to clone
                    StrippedDependency rootDep = StrippedDependency.GetDependency(_options.RepoUri, _options.Version);
                    accumulatedDependencies.Add(rootDep);
                    Logger.LogInformation($"Starting deep clone of {rootDep.RepoUri}@{rootDep.Commit}");
                }

                while (accumulatedDependencies.Any())
                {
                    // add this level's dependencies to the queue and clear it for the next level
                    foreach (StrippedDependency d in accumulatedDependencies)
                    {
                        dependenciesToClone.Enqueue(d);
                    }
                    accumulatedDependencies.Clear();

                    // this will do one level of clones at a time
                    while (dependenciesToClone.Any())
                    {
                        StrippedDependency repo = dependenciesToClone.Dequeue();
                        // the folder for the specific repo-hash we are cloning.  these will be orphaned from the .gitdir.
                        string repoPath = GetRepoDirectory(_options.ReposFolder, repo.RepoUri, repo.Commit);
                        // the "master" folder, which continues to be linked to the .git directory
                        string masterGitRepoPath = GetMasterGitRepoPath(_options.ReposFolder, repo.RepoUri);
                        // the .gitdir that is shared among all repo-hashes (temporarily, before they are orphaned)
                        string masterRepoGitDirPath = GetMasterGitDirPath(_options.GitDirFolder, repo.RepoUri);
                        // used for the specific-commit version of the repo
                        Local local;

                        // Scenarios we handle: no/existing/orphaned master folder cross no/existing .gitdir
                        await HandleMasterCopy(remoteFactory, repo.RepoUri, masterGitRepoPath, masterRepoGitDirPath, Logger);
                        // if using the default .gitdir path, get that for use in the specific clone.
                        if (masterRepoGitDirPath == null)
                        {
                            masterRepoGitDirPath = GetDefaultMasterGitDirPath(_options.ReposFolder, repo.RepoUri);
                        }
                        local = HandleRepoAtSpecificHash(repoPath, repo.Commit, masterRepoGitDirPath, Logger);

                        Logger.LogDebug($"Starting to look for dependencies in {repoPath}");
                        try
                        {
                            IEnumerable<DependencyDetail> deps = await local.GetDependenciesAsync();
                            IEnumerable<DependencyDetail> filteredDeps = FilterToolsetDependencies(deps, _options.IncludeToolset, Logger);
                            Logger.LogDebug($"Got {deps.Count()} dependencies and filtered to {filteredDeps.Count()} dependencies");
                            foreach (DependencyDetail d in filteredDeps)
                            {
                                StrippedDependency dep = StrippedDependency.GetDependency(d);
                                // e.g. arcade depends on previous versions of itself to build, so this would go on forever
                                if (d.RepoUri == repo.RepoUri)
                                {
                                    Logger.LogDebug($"Skipping self-dependency in {repo.RepoUri} ({repo.Commit} => {d.Commit})");
                                }
                                // circular dependencies that have different hashes, e.g. DotNet-Trusted -> core-setup -> DotNet-Trusted -> ...
                                else if (dep.HasDependencyOn(repo))
                                {
                                    Logger.LogDebug($"Skipping already-seen circular dependency from {repo.RepoUri} to {d.RepoUri}");
                                }
                                else if (_options.IgnoredRepos.Any(r => r.Equals(d.RepoUri, StringComparison.OrdinalIgnoreCase)))
                                {
                                    Logger.LogDebug($"Skipping ignored repo {d.RepoUri} (at {d.Commit})");
                                }
                                else if (string.IsNullOrWhiteSpace(d.Commit))
                                {
                                    Logger.LogWarning($"Skipping dependency from {repo.RepoUri}@{repo.Commit} to {d.RepoUri}: Missing commit.");
                                }
                                else
                                {
                                    StrippedDependency stripped = StrippedDependency.GetDependency(d);
                                    Logger.LogDebug($"Adding new dependency {stripped.RepoUri}@{stripped.Commit}");
                                    repo.AddDependency(dep);
                                    accumulatedDependencies.Add(stripped);
                                }
                            }
                            // delete the .gitdir redirect to orphan the repo.
                            // we want to do this because otherwise all of these folder will show as dirty in Git,
                            // and any operations on them will affect the master copy and all the others, which
                            // could be confusing.
                            string repoGitRedirectPath = Path.Combine(repoPath, ".git");
                            if (File.Exists(repoGitRedirectPath))
                            {
                                File.Delete(repoGitRedirectPath);
                            }
                        }
                        catch (DirectoryNotFoundException)
                        {
                            Logger.LogWarning($"Repo {repoPath} appears to have no '/eng' directory at commit {repo.Commit}.  Dependency chain is broken here.");
                        }
                        catch (FileNotFoundException)
                        {
                            Logger.LogWarning($"Repo {repoPath} appears to have no '/eng/Version.Details.xml' file at commit {repo.Commit}.  Dependency chain is broken here.");
                        }

                        Logger.LogDebug($"Now have {dependenciesToClone.Count} dependencies to consider");
                    }   // end inner while(dependenciesToClone.Any())


                    if (_options.CloneDepth == 0 && accumulatedDependencies.Any())
                    {
                        Logger.LogInformation($"Reached clone depth limit, aborting with {accumulatedDependencies.Count} dependencies remaining");
                        foreach (StrippedDependency d in accumulatedDependencies)
                        {
                            Logger.LogDebug($"Abandoning dependency {d.RepoUri}@{d.Commit}");
                        }
                        break;
                    }
                    else
                    {
                        _options.CloneDepth--;
                        Logger.LogDebug($"Clone depth remaining: {_options.CloneDepth}");
                        Logger.LogDebug($"Dependencies remaining: {accumulatedDependencies.Count}");
                    }
                }   // end outer while(accumulatedDependencies.Any())

                return Constants.SuccessCode;
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, "Something failed while cloning.");
                return Constants.ErrorCode;
            }
        }

        private static Local HandleRepoAtSpecificHash(string repoPath, string commit, string masterRepoGitDirPath, ILogger log)
        {
            Local local;

            if (Directory.Exists(repoPath))
            {
                log.LogDebug($"Repo path {repoPath} already exists, assuming we cloned already and skipping");
                local = new Local(log, repoPath);
            }
            else
            {
                log.LogDebug($"Setting up {repoPath} with .gitdir redirect");
                Directory.CreateDirectory(repoPath);
                File.WriteAllText(Path.Combine(repoPath, ".git"), GetGitDirRedirectString(masterRepoGitDirPath));
                log.LogInformation($"Checking out {commit} in {repoPath}");
                local = new Local(log, repoPath);
                local.Checkout(commit, true);
            }

            return local;
        }

        private async static Task HandleMasterCopy(RemoteFactory remoteFactory, string repoUrl, string masterGitRepoPath, string masterRepoGitDirPath, ILogger log)
        {
            if (masterRepoGitDirPath != null)
            {
                await HandleMasterCopyWithGitDirPath(remoteFactory, repoUrl, masterGitRepoPath, masterRepoGitDirPath, log);
            }
            else
            {
                await HandleMasterCopyWithDefaultGitDir(remoteFactory, repoUrl, masterGitRepoPath, masterRepoGitDirPath, log);
            }
        }

        private static async Task HandleMasterCopyWithDefaultGitDir(RemoteFactory remoteFactory, string repoUrl, string masterGitRepoPath, string masterRepoGitDirPath, ILogger log)
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

        private static async Task HandleMasterCopyWithGitDirPath(RemoteFactory remoteFactory, string repoUrl, string masterGitRepoPath, string masterRepoGitDirPath, ILogger log)
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

        private static async Task HandleMasterCopyAndCreateGitDir(RemoteFactory remoteFactory, string repoUrl, string masterGitRepoPath, string masterRepoGitDirPath, string gitDirRedirect, ILogger log)
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

        private static void EnsureOptionsCompatibility(CloneCommandLineOptions options)
        {

            if ((string.IsNullOrWhiteSpace(options.RepoUri) && !string.IsNullOrWhiteSpace(options.Version)) ||
                (!string.IsNullOrWhiteSpace(options.RepoUri) && string.IsNullOrWhiteSpace(options.Version)))
            {
                throw new ArgumentException($"Either specify both repo and version to clone a specific remote repo, or neither to clone all dependencies from this repo.");
            }

            if (string.IsNullOrWhiteSpace(options.ReposFolder))
            {
                options.ReposFolder = Environment.CurrentDirectory;
            }

            if (!Directory.Exists(options.ReposFolder))
            {
                Directory.CreateDirectory(options.ReposFolder);
            }

            if (options.GitDirFolder != null && !Directory.Exists(options.GitDirFolder))
            {
                Directory.CreateDirectory(options.GitDirFolder);
            }
        }

        private static string GetRepoDirectory(string reposFolder, string repoUri, string commit)
        {
            if (repoUri.EndsWith(".git"))
            {
                repoUri = repoUri.Substring(0, repoUri.Length - ".git".Length);
            }

            // commit could actually be a branch or tag, make it filename-safe
            commit = commit.Replace('/', '-').Replace('\\', '-').Replace('?', '-').Replace('*', '-').Replace(':', '-').Replace('|', '-').Replace('"', '-').Replace('<', '-').Replace('>', '-');
            return Path.Combine(reposFolder, $"{repoUri.Substring(repoUri.LastIndexOf("/") + 1)}.{commit}");
        }

        private static string GetMasterGitDirPath(string gitDirParent, string repoUri)
        {
            if (gitDirParent == null)
            {
                return null;
            }

            if (repoUri.EndsWith(".git"))
            {
                repoUri = repoUri.Substring(0, repoUri.Length - ".git".Length);
            }

            return Path.Combine(gitDirParent, $"{repoUri.Substring(repoUri.LastIndexOf("/") + 1)}.git");
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

        private static IEnumerable<DependencyDetail> FilterToolsetDependencies(IEnumerable<DependencyDetail> dependencies, bool includeToolset, ILogger log)
        {
            if (!includeToolset)
            {
                log.LogInformation($"Removing toolset dependencies...");
                return dependencies.Where(dependency => dependency.Type != DependencyType.Toolset);
            }
            return dependencies;
        }

        private class StrippedDependency
        {
            internal string RepoUri { get; set; }
            internal string Commit { get; set; }
            private bool Visited { get; set; }
            internal HashSet<StrippedDependency> Dependencies { get; set; }
            internal static HashSet<StrippedDependency> AllDependencies;

            static StrippedDependency()
            {
                AllDependencies = new HashSet<StrippedDependency>();
            }

            internal static StrippedDependency GetDependency(DependencyDetail d)
            {
                return GetDependency(d.RepoUri, d.Commit);
            }

            internal static StrippedDependency GetDependency(StrippedDependency d)
            {
                return GetDependency(d.RepoUri, d.Commit);
            }

            internal static StrippedDependency GetDependency(string repoUrl, string commit)
            {
                StrippedDependency dep;
                dep = AllDependencies.SingleOrDefault(d => d.RepoUri.ToLowerInvariant() == repoUrl.ToLowerInvariant() && d.Commit.ToLowerInvariant() == commit.ToLowerInvariant());
                if (dep == null)
                {
                    dep = new StrippedDependency(repoUrl, commit);
                    foreach (StrippedDependency previousDep in AllDependencies.Where(d => d.RepoUri.ToLowerInvariant() == repoUrl.ToLowerInvariant()).SelectMany(d => d.Dependencies))
                    {
                        dep.AddDependency(previousDep);
                    }
                    AllDependencies.Add(dep);
                }
                return dep;
            }

            private StrippedDependency(string repoUrl, string commit)
            {
                this.RepoUri = repoUrl;
                this.Commit = commit;
                this.Dependencies = new HashSet<StrippedDependency>();
                this.Dependencies.Add(this);
            }

            private StrippedDependency(DependencyDetail d) : this(d.RepoUri, d.Commit) { }

            internal void AddDependency(StrippedDependency dep)
            {
                StrippedDependency other = GetDependency(dep);
                if (this.Dependencies.Any(d => d.RepoUri.ToLowerInvariant() == other.RepoUri.ToLowerInvariant()))
                {
                    return;
                }
                this.Dependencies.Add(other);
                foreach (StrippedDependency sameUrl in AllDependencies.Where(d => d.RepoUri.ToLowerInvariant() == this.RepoUri.ToLowerInvariant()))
                {
                    sameUrl.Dependencies.Add(other);
                }
            }

            internal void AddDependency(DependencyDetail dep)
            {
                this.AddDependency(GetDependency(dep));
            }

            internal bool HasDependencyOn(string repoUrl)
            {
                bool hasDep = false;
                lock (AllDependencies)
                {
                    foreach (StrippedDependency dep in this.Dependencies)
                    {
                        if (dep.Visited)
                        {
                            return false;
                        }
                        if (dep.RepoUri.ToLowerInvariant() == this.RepoUri.ToLowerInvariant())
                        {
                            return false;
                        }
                        dep.Visited = true;
                        hasDep = hasDep || dep.RepoUri.ToLowerInvariant() == repoUrl.ToLowerInvariant() || dep.HasDependencyOn(repoUrl);
                        if (hasDep)
                        {
                            break;
                        }
                    }
                    foreach (StrippedDependency dep in AllDependencies)
                    {
                        dep.Visited = false;
                    }
                }

                return hasDep;
            }

            internal bool HasDependencyOn(StrippedDependency dep)
            {
                return HasDependencyOn(dep.RepoUri);
            }

            internal bool HasDependencyOn(DependencyDetail dep)
            {
                return HasDependencyOn(dep.RepoUri);
            }

            public override bool Equals(object obj)
            {
                StrippedDependency other = obj as StrippedDependency;
                if (other == null)
                {
                    return false;
                }
                return this.RepoUri == other.RepoUri && this.Commit == other.Commit;
            }

            public override int GetHashCode()
            {
                return this.RepoUri.GetHashCode() ^ this.Commit.GetHashCode();
            }

            public override string ToString()
            {
                return $"{this.RepoUri}@{this.Commit} ({this.Dependencies.Count} deps)";
            }
        }
    }
}
