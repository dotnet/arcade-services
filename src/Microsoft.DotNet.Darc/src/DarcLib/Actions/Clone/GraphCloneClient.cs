// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class GraphCloneClient
    {
        private const string GitDirRedirectPrefix = "gitdir: ";

        public string GitDir { get; set; }

        public ILogger Logger { get; set; }
        public IRemoteFactory RemoteFactory { get; set; }

        public async Task<object> GetGraphAsync(
            IEnumerable<StrippedDependency> rootDependencies,
            IEnumerable<string> ignoredRepos,
            bool includeToolset,
            bool forceCoherence,
            uint cloneDepth)
        {
            var accumulatedDependencies = new HashSet<StrippedDependency>(rootDependencies);

            // At the end of each depth level, the accumulated deps are moved to this queue to be cloned.
            var dependenciesToClone = new Queue<StrippedDependency>();

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
                    //// the folder for the specific repo-hash we are cloning.  these will be orphaned from the .gitdir.
                    //string repoPath = GetRepoDirectory(ReposDir, repo.RepoUri, repo.Commit);
                    //// the "master" folder, which continues to be linked to the .git directory
                    //string masterGitRepoPath = GetMasterGitRepoPath(ReposDir, repo.RepoUri);
                    // the .gitdir that is shared among all repo-hashes (temporarily, before they are orphaned)
                    string masterRepoGitDirPath = GetMasterGitDirPath(repo.RepoUri);

                    // Create the bare repo clone.
                    if (!Directory.Exists(masterRepoGitDirPath))
                    {
                        IRemote repoRemote = await RemoteFactory.GetRemoteAsync(repo.RepoUri, Logger);
                        repoRemote.Clone(repo.RepoUri, null, null, masterRepoGitDirPath);
                    }

                    Logger.LogDebug($"Starting to look for dependencies in {masterRepoGitDirPath}");
                    try
                    {
                        IEnumerable<DependencyDetail> deps =
                            (await local.GetDependenciesAsync(branch: repo.Commit)).ToArray();

                        Logger.LogDebug($"Got {deps.Count()} dependencies.");

                        if (!includeToolset)
                        {
                            Logger.LogInformation($"Removing toolset dependencies...");
                            deps = deps.Where(dependency => dependency.Type != DependencyType.Toolset);
                            Logger.LogDebug($"Filtered toolset dependencies. Now {deps.Count()} dependencies");
                        }

                        foreach (DependencyDetail d in deps)
                        {
                            StrippedDependency dep = StrippedDependency.GetOrAddDependency(d);
                            // Remove self-dependency. E.g. arcade depends on previous versions of itself to build, so this would go on forever.
                            if (d.RepoUri == repo.RepoUri)
                            {
                                Logger.LogDebug($"Skipping self-dependency in {repo.RepoUri} ({repo.Commit} => {d.Commit})");
                            }
                            // Remove circular dependencies that have different hashes, e.g. DotNet-Trusted -> core-setup -> DotNet-Trusted -> ...
                            else if (dep.HasDependencyOn(repo))
                            {
                                Logger.LogDebug($"Skipping already-seen circular dependency from {repo.RepoUri} to {d.RepoUri}");
                            }
                            else if (ignoredRepos.Any(r => r.Equals(d.RepoUri, StringComparison.OrdinalIgnoreCase)))
                            {
                                Logger.LogDebug($"Skipping ignored repo {d.RepoUri} (at {d.Commit})");
                            }
                            else if (string.IsNullOrWhiteSpace(d.Commit))
                            {
                                Logger.LogWarning($"Skipping dependency from {repo.RepoUri}@{repo.Commit} to {d.RepoUri}: Missing commit.");
                            }
                            else
                            {
                                StrippedDependency stripped = StrippedDependency.GetOrAddDependency(d);
                                Logger.LogDebug($"Adding new dependency {stripped.RepoUri}@{stripped.Commit}");
                                repo.AddDependency(dep);
                                accumulatedDependencies.Add(stripped);
                            }
                        }

                        Logger.LogDebug($"done looking for dependencies in {repoPath} at {repo.Commit}");
                    }
                    catch (DirectoryNotFoundException)
                    {
                        Logger.LogWarning($"Repo {repoPath} appears to have no '/eng' directory at commit {repo.Commit}.  Dependency chain is broken here.");
                    }
                    catch (FileNotFoundException)
                    {
                        Logger.LogWarning($"Repo {repoPath} appears to have no '/eng/Version.Details.xml' file at commit {repo.Commit}.  Dependency chain is broken here.");
                    }
                    finally
                    {
                        // delete the .gitdir redirect to orphan the repo.
                        // we want to do this because otherwise all of these folder will show as dirty in Git,
                        // and any operations on them will affect the master copy and all the others, which
                        // could be confusing.
                        string repoGitRedirectPath = Path.Combine(repoPath, ".git");
                        if (File.Exists(repoGitRedirectPath))
                        {
                            Logger.LogDebug($"Deleting .gitdir redirect {repoGitRedirectPath}");
                            File.Delete(repoGitRedirectPath);
                        }
                        else
                        {
                            Logger.LogDebug($"No .gitdir redirect found at {repoGitRedirectPath}");
                        }
                    }
                } // end inner while(dependenciesToClone.Any())


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
            } // end outer while(accumulatedDependencies.Any())

            return null;
        }

        public async Task CreateWorkTreesAsync(object graph, string optionsReposFolder)
        {

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
