// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CSharpx;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
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
                // use a set to keep track of whether we've seen dependencies before, otherwise we get trapped in circular dependencies
                HashSet<StrippedDependency> seenDependencies = new HashSet<StrippedDependency>();
                // list of master copies of repos that we relocate the .gitdirs for later
                HashSet<string> masterCopies = new HashSet<string>();
                RemoteFactory remoteFactory = new RemoteFactory(_options);

                if (string.IsNullOrWhiteSpace(_options.RepoUri))
                {
                    Local local = new Local(Logger);
                    IEnumerable<DependencyDetail>  rootDependencies = await local.GetDependenciesAsync();
                    IEnumerable<StrippedDependency> stripped = rootDependencies.Select(d => new StrippedDependency(d));
                    stripped.ForEach((s) => accumulatedDependencies.Add(s));
                    stripped.ForEach((s) => seenDependencies.Add(s));
                    Logger.LogInformation($"Found {rootDependencies.Count()} local dependencies.  Starting deep clone...");
                }
                else
                {
                    // Start with the root repo we were asked to clone
                    StrippedDependency rootDep = new StrippedDependency(_options.RepoUri, _options.Version);
                    accumulatedDependencies.Add(rootDep);
                    seenDependencies.Add(rootDep);
                    Logger.LogInformation($"Starting deep clone of {rootDep.RepoUri}@{rootDep.Commit}");
                }

                while (accumulatedDependencies.Any())
                {
                    // add this level's dependencies to the queue and clear it for the next level
                    accumulatedDependencies.ForEach(dependenciesToClone.Enqueue);
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
                            IEnumerable<DependencyDetail> filteredDeps = FilterToolsetDependencies(deps, _options.IncludeToolset);
                            Logger.LogDebug($"Got {deps.Count()} dependencies and filtered to {filteredDeps.Count()} dependencies");
                            filteredDeps.ForEach((d) =>
                            {
                                // e.g. arcade depends on previous versions of itself to build, so this would go on forever
                                if (d.RepoUri == repo.RepoUri)
                                {
                                    Logger.LogDebug($"Skipping self-dependency in {repo.RepoUri} ({repo.Commit} => {d.Commit})");
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
                                    StrippedDependency stripped = new StrippedDependency(d);
                                    if (!seenDependencies.Contains(stripped))
                                    {
                                        Logger.LogDebug($"Adding new dependency {stripped.RepoUri}@{stripped.Commit}");
                                        seenDependencies.Add(stripped);
                                        accumulatedDependencies.Add(stripped);
                                    }
                                }
                            });
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
                string gitDirRedirect = GetGitDirRedirectString(masterRepoGitDirPath);
                log.LogDebug($"Starting master copy for {repoUrl} in {masterGitRepoPath} with .gitdir {masterRepoGitDirPath}");

                // the .gitdir exists already.  We are resuming with the same --git-dir-parent setting.
                if (Directory.Exists(masterRepoGitDirPath))
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
                // The .gitdir does not exist.  This could be a new clone or resuming with a different --git-dir-parent setting.
                else
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
            }
            else // masterRepoGitDirPath == null
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

        private static IEnumerable<DependencyDetail> FilterToolsetDependencies(IEnumerable<DependencyDetail> dependencies, bool includeToolset)
        {
            if (!includeToolset)
            {
                Console.WriteLine($"Removing toolset dependencies...");
                return dependencies.Where(dependency => dependency.Type != DependencyType.Toolset);
            }
            return dependencies;
        }

        private class StrippedDependency
        {
            internal string RepoUri { get; set; }
            internal string Commit { get; set; }

            internal StrippedDependency(DependencyDetail d)
            {
                this.RepoUri = d.RepoUri;
                this.Commit = d.Commit;
            }

            internal StrippedDependency(string repoUri, string commit)
            {
                this.RepoUri = repoUri;
                this.Commit = commit;
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
        }
    }
}
