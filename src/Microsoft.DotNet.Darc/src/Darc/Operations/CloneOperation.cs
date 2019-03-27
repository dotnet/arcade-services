using CSharpx;
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
                // use a queue to accumulate dependencies as we go
                Queue<StrippedDependency> dependenciesToClone = new Queue<StrippedDependency>();
                // use a set to keep track of whether we've seen dependencies before, otherwise we get trapped in circular dependencies
                HashSet<StrippedDependency> seenDependencies = new HashSet<StrippedDependency>();
                RemoteFactory remoteFactory = new RemoteFactory(_options);
                
                if (string.IsNullOrWhiteSpace(_options.ReposFolder))
                {
                    _options.ReposFolder = Environment.CurrentDirectory;
                }

                if (!Directory.Exists(_options.ReposFolder))
                {
                    Directory.CreateDirectory(_options.ReposFolder);
                }

                // Start with the root repo we were asked to clone
                dependenciesToClone.Enqueue(new StrippedDependency(_options.RepoUri, _options.Version));
                
                while (dependenciesToClone.Any())
                {
                    StrippedDependency repo = dependenciesToClone.Dequeue();
                    string repoPath = GetRepoDirectory(_options.ReposFolder, repo.RepoUri, repo.Commit);
                    if (Directory.Exists(repoPath))
                    {
                        Logger.LogDebug($"Repo path {repoPath} already exists, assuming we cloned already and skipping");
                    }
                    else
                    {
                        Logger.LogInformation($"Cloning {repo.RepoUri}@{repo.Commit} into {repoPath}");
                        IRemote repoRemote = await remoteFactory.GetRemoteAsync(repo.RepoUri, Logger);
                        await repoRemote.CloneAsync(repo.RepoUri, repo.Commit, repoPath);
                    }

                    Logger.LogDebug($"Starting to look for dependencies in {repoPath}");
                    Local local = new Local(Logger, repoPath);

                    try
                    {
                        IEnumerable<DependencyDetail> deps = await local.GetDependenciesAsync();
                        IEnumerable<DependencyDetail> filteredDeps = FilterToolsetDependencies(deps);
                        Logger.LogDebug($"Got {deps.Count()} dependencies and filtered to {filteredDeps.Count()} dependencies");
                        filteredDeps.ForEach((d) => {
                            // arcade depends on previous versions of itself to build, so this would go on forever
                            if (d.RepoUri == repo.RepoUri)
                            {
                                Logger.LogDebug($"Skipping self-dependency in {repo.RepoUri} ({repo.Commit} => {d.Commit})");
                            }
                            else
                            {
                                StrippedDependency stripped = new StrippedDependency(d);
                                if (!seenDependencies.Contains(stripped))
                                {
                                    Logger.LogDebug($"Adding new dependency {stripped.RepoUri}@{stripped.Commit}");
                                    seenDependencies.Add(stripped);
                                    dependenciesToClone.Enqueue(stripped);
                                }
                            }
                        });
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
                }


                return Constants.SuccessCode;
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, "Something failed while cloning.");
                return Constants.ErrorCode;
            }
        }

        private string GetRepoDirectory(string reposFolder, string repoUri, string commit)
        {
            // commit could actually be a branch or tag, make it filename-safe
            commit = commit.Replace('/', '-').Replace('\\', '-').Replace('?', '-').Replace('*', '-').Replace(':', '-').Replace('|', '-').Replace('"', '-').Replace('<', '-').Replace('>', '-');
            return Path.Combine(reposFolder, $"{repoUri.Substring(repoUri.LastIndexOf("/") + 1)}.{commit}");
        }

        private IEnumerable<DependencyDetail> FilterToolsetDependencies(IEnumerable<DependencyDetail> dependencies)
        {
            if (!_options.IncludeToolset)
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
