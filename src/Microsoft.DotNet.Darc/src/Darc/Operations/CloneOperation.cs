using CSharpx;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
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
                EnsureOptionsCompatibility(_options);
                RemoteFactory remoteFactory = new RemoteFactory(_options);
                HashSet<StrippedDependency> seenDependencies = new HashSet<StrippedDependency>();

                if (_options.Local)
                {
                    Local local = new Local(Logger);
                    IEnumerable<DependencyDetail>  rootDependencies = await local.GetDependenciesAsync();

                    foreach (DependencyDetail dep in rootDependencies)
                    {
                        StrippedDependency seen = new StrippedDependency(dep);
                        if (seenDependencies.Contains(seen))
                        {
                            Logger.LogDebug($"Local dependency {dep.RepoUri}@{dep.Commit} already processed.  Skipping...");
                        }
                        else
                        {
                            seenDependencies.Add(seen);
                            Logger.LogInformation($"Found local dependency {dep.RepoUri}@{dep.Commit}.  Starting deep clone...");
                            await CloneRemoteRepoAndDependencies(remoteFactory, _options.ReposFolder, dep.RepoUri, dep.Commit, _options.IncludeToolset, Logger);
                        }
                    }
                }
                else
                {
                    Logger.LogInformation($"Starting deep clone of {_options.RepoUri}@{_options.Version}");
                    await CloneRemoteRepoAndDependencies(remoteFactory, _options.ReposFolder, _options.RepoUri, _options.Version, _options.IncludeToolset, Logger);
                }

                return Constants.SuccessCode;
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, "Something failed while cloning.");
                return Constants.ErrorCode;
            }
        }

        private static async Task CloneRemoteRepoAndDependencies(RemoteFactory remoteFactory, string reposFolder, string repoUri, string commit, bool includeToolset, ILogger logger)
        {
            IRemote rootRepoRemote = await remoteFactory.GetRemoteAsync(repoUri, logger);
            IEnumerable<DependencyDetail> rootDependencies = await rootRepoRemote.GetDependenciesAsync(repoUri, commit);
            rootDependencies = FilterToolsetDependencies(rootDependencies, includeToolset);

            if (!rootDependencies.Any())
            {
                string repoPath = GetRepoDirectory(reposFolder, repoUri, commit);
                if (Directory.Exists(repoPath))
                {
                    logger.LogDebug($"Repo path {repoPath} already exists, assuming we cloned already and skipping");
                }
                else
                {
                    logger.LogInformation($"Remote repo {repoUri}@{commit} has no dependencies.  Cloning shallowly into {repoPath}");
                    IRemote repoRemote = await remoteFactory.GetRemoteAsync(repoUri, logger);
                    await repoRemote.CloneAsync(repoUri, commit, repoPath);
                }
                return;
            }

            DependencyGraphBuildOptions graphBuildOptions = new DependencyGraphBuildOptions()
            {
                IncludeToolset = includeToolset,
                LookupBuilds = true,
                NodeDiff = NodeDiff.None
            };

            logger.LogDebug($"Building depdendency graph for {repoUri}@{commit} with {rootDependencies.Count()} dependencies");
            DependencyGraph graph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
                remoteFactory,
                rootDependencies,
                repoUri,
                commit,
                graphBuildOptions,
                logger);

            foreach (DependencyGraphNode repo in graph.Nodes)
            {
                string repoPath = GetRepoDirectory(reposFolder, repo.RepoUri, repo.Commit);
                if (Directory.Exists(repoPath))
                {
                    logger.LogDebug($"Repo path {repoPath} already exists, assuming we cloned already and skipping");
                }
                else
                {
                    logger.LogInformation($"Cloning {repo.RepoUri}@{repo.Commit} into {repoPath}");
                    IRemote repoRemote = await remoteFactory.GetRemoteAsync(repo.RepoUri, logger);
                    await repoRemote.CloneAsync(repo.RepoUri, repo.Commit, repoPath);
                }
            }
        }

        private static void EnsureOptionsCompatibility(CloneCommandLineOptions options)
        {
            if (options.Local)
            {
                if (!string.IsNullOrWhiteSpace(options.RepoUri) || !string.IsNullOrWhiteSpace(options.Version))
                {
                    throw new ArgumentException($"If using the local Version.Details.xml to determine repos to clone, repo-uri and version are not applicable.");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(options.RepoUri))
                {
                    throw new ArgumentException($"If not using a local Version.Details.xml, the repo to start the clone at must be specified.");
                }
                if (string.IsNullOrWhiteSpace(options.Version))
                {
                    throw new ArgumentException($"If not using a local Version.Details.xml, the version to start the clone at must be specified.");
                }
            }

            if (string.IsNullOrWhiteSpace(options.ReposFolder))
            {
                options.ReposFolder = Environment.CurrentDirectory;
            }

            if (!Directory.Exists(options.ReposFolder))
            {
                Directory.CreateDirectory(options.ReposFolder);
            }
        }

        private static string GetRepoDirectory(string reposFolder, string repoUri, string commit)
        {
            // commit could actually be a branch or tag, make it filename-safe
            commit = commit.Replace('/', '-').Replace('\\', '-').Replace('?', '-').Replace('*', '-').Replace(':', '-').Replace('|', '-').Replace('"', '-').Replace('<', '-').Replace('>', '-');
            return Path.Combine(reposFolder, $"{repoUri.Substring(repoUri.LastIndexOf("/") + 1)}.{commit}");
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
