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
                        string repoPath = GetRepoDirectory(_options.ReposFolder, repo.RepoUri, repo.Commit);
                        string masterGitRepoPath = GetMasterGitRepoPath(_options.ReposFolder, repo.RepoUri);
                        string masterRepoGitDirPath = GetMasterGitDirPath(_options.GitDirFolder, repo.RepoUri);
                        // used for the specific-commit version of the repo
                        Local local;

                        if (!Directory.Exists(masterGitRepoPath))
                        {
                            Logger.LogInformation($"Cloning master copy of {repo.RepoUri} into {masterGitRepoPath}");
                            IRemote repoRemote = await remoteFactory.GetRemoteAsync(repo.RepoUri, Logger);
                            repoRemote.Clone(repo.RepoUri, null, masterGitRepoPath, masterRepoGitDirPath);
                        }
                        if (Directory.Exists(repoPath))
                        {
                            Logger.LogDebug($"Repo path {repoPath} already exists, assuming we cloned already and skipping");
                            local = new Local(Logger, repoPath);
                        }
                        else
                        {
                            Logger.LogDebug($"Copying master copy {masterGitRepoPath} into {repoPath}");
                            CopyDirectory(masterGitRepoPath, repoPath);
                            Logger.LogInformation($"Checking out {repo.Commit} in {repoPath}");
                            local = new Local(Logger, repoPath);
                            local.Checkout(repo.Commit, true);
                        }

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

        private static string GetMasterGitRepoPath(string reposFolder, string repoUri)
        {
            if (repoUri.EndsWith(".git"))
            {
                repoUri = repoUri.Substring(0, repoUri.Length - ".git".Length);
            }
            return Path.Combine(reposFolder, $"{repoUri.Substring(repoUri.LastIndexOf("/") + 1)}");
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

        // https://docs.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories
        private static void CopyDirectory(string sourceDirPath, string destDirPath)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirPath);

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirPath))
            {
                Directory.CreateDirectory(destDirPath);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirPath, file.Name);
                file.CopyTo(temppath, false);
            }

            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destDirPath, subdir.Name);
                CopyDirectory(subdir.FullName, temppath);
            }
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
