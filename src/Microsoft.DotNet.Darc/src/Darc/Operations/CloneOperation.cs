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

                if (string.IsNullOrWhiteSpace(_options.RepoUri))
                {
                    Local local = new Local(Logger);
                    IEnumerable<DependencyDetail>  rootDependencies = await local.GetDependenciesAsync();

                    foreach (DependencyDetail dep in rootDependencies)
                    {
                        Logger.LogInformation($"Found local dependency {dep.RepoUri}@{dep.Commit}.  Starting deep clone...");
                        await CloneRemoteRepoAndDependencies(remoteFactory, _options.ReposFolder, dep.RepoUri, dep.Commit, _options.IncludeToolset, Logger);
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
                    repoRemote.Clone(repoUri, commit, repoPath);
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
                string repoPath = GetRepoDirectory(reposFolder, repo.Repository, repo.Commit);
                if (Directory.Exists(repoPath))
                {
                    logger.LogDebug($"Repo path {repoPath} already exists, assuming we cloned already and skipping");
                }
                else
                {
                    logger.LogInformation($"Cloning {repo.Repository}@{repo.Commit} into {repoPath}");
                    IRemote repoRemote = await remoteFactory.GetRemoteAsync(repo.Repository, logger);
                    repoRemote.Clone(repo.Repository, repo.Commit, repoPath);
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
    }
}
