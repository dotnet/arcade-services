// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Actions.Clone;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Models.Darc;

namespace Microsoft.DotNet.Darc.Operations.Clone
{
    /// <summary>
    /// This operation has two modes:
    /// 
    /// - In remote mode, it will clone a single remote repo, then recursively fetch all repos it
    ///   depends on.
    /// - In local mode, it will take all the dependencies in the current repo's
    ///   Version.Details.xml, clone them, and recursively fetch repos that they depend on.
    /// 
    /// For performance reasons, we do some gymnastics so that we only ever have to actually clone
    /// a repo once.
    /// 
    /// For user convenience reasons, the generally-intended mode of operation is to use a separate
    /// folder for .gitdirs, which allows a developer to do a "git clean" and not have to reclone
    /// all of the repos again for the next build. This is also useful for performance.
    ///
    /// The general approach is:
    /// 
    /// 1. Clone a "master" version of a repo.  LibGit2 does not currently support cloning with a
    ///    separate .gitdir, so we then:
    /// 2. Move the .gitdir to the specified location.  If there is none specified, the .gitdir
    ///    stays in the repo as usual.
    /// 3. Create a file (not directory) called ".git" pointing to the new .gitdir we just moved.
    ///    This allows Git to use the "master" folder properly as a repo.
    ///      Note: The "master" folder is also the only one that will end up being a real Git repo
    ///      at the end of the process.
    /// 4. For each commit we want to check out, create a new folder called repo.hash and add the
    ///    same .gitdir redirect as above.
    /// 5. Checkout the desired commit in that folder.  This causes a minor change in the "master"
    ///    folder (it changes the HEAD), but it is otherwise unaffected.
    /// 6. Delete the .gitdir redirect file in the new repo.hash folder to unlink it from the
    ///    "master" folder.  This is done to avoid confusion where every repo.hash folder shows up
    ///    in Git as having dirty files (since the repo thinks its HEAD is at a different commit).
    /// 
    /// Submodules inside repo.hash folders add some complexity to this process.  Once a submodule
    /// has been initialized in the .gitdir, it will be initialized for all repos; however, the
    /// submodule will not have one of the .gitdir redirects. In this case, we go back to the
    /// "master" folder, which will have a .git/modules/subrepo directory, and redirect the
    /// submodule to this.  This means that if a submodule is included in multiple repos, we will
    /// have multiple copies of it, but this hasn't seemed to have a large perf impact.
    /// 
    /// After cloning, we use local dependency graph discovery to add new repo-hash combinations to
    /// clone at.  This is done in waves to support a "depth limit" of followed repos.
    /// </summary>
    internal class CloneOperation : Operation
    {
        private CloneCommandLineOptions _options;

        private GraphCloneClient _cloneClient;

        public CloneOperation(
            CloneCommandLineOptions options,
            GraphCloneClient cloneClient = null)
            : base(options)
        {
            _options = options;
            _cloneClient = cloneClient ?? new GraphCloneClient
            {
                GitDir = _options.GitDirFolder,
                RemoteFactory = new RemoteFactory(_options),
                Logger = Logger,
                IncludeToolset = _options.IncludeToolset,
                SkipFetch = _options.SkipFetch,
            };
        }

        public override async Task<int> ExecuteAsync()
        {
            try
            {
                EnsureOptionsCompatibility(_options);
                // Accumulate root info.
                SourceBuildNode root;

                // Seed the dependency set with initial dependencies from args.
                if (string.IsNullOrWhiteSpace(_options.RepoUri))
                {
                    Local local = new Local(Logger);

                    var rootDependencyXml = await local.GetDependencyFileXmlContentAsync();

                    _cloneClient.RootOverrides =
                        DarcCloneOverrideDetail.ParseAll(rootDependencyXml.DocumentElement);

                    root = _cloneClient.ParseNode(
                        new SourceBuildIdentity { RepoUri = "root" },
                        rootDependencyXml);

                    Logger.LogInformation($"Found {root.UpstreamEdges} local dependency edges.  Starting deep clone...");
                }
                else
                {
                    // Start with the root repo we were asked to clone
                    var targetIdentity = new SourceBuildIdentity
                    {
                        RepoUri = _options.RepoUri,
                        Commit = _options.Version
                    };

                    var rootId = new SourceBuildIdentity
                    {
                        RepoUri = "root"
                    };

                    root = new SourceBuildNode
                    {
                        Identity = rootId,
                        UpstreamEdges = new[]
                        {
                            new SourceBuildEdge
                            {
                                Downstream = rootId,
                                Upstream = targetIdentity
                            }
                        }
                    };

                    Logger.LogInformation($"Starting deep clone of {targetIdentity}");
                }

                var timeBeforeGraph = DateTimeOffset.UtcNow;

                SourceBuildGraph graph = await _cloneClient.GetGraphAsync(
                    root,
                    _options.IgnoredRepos,
                    _options.CloneDepth);

                // Keep some metadata for later debugging or unit test setup if necessary.
                WriteGraphDebugInfoToFiles(
                    Path.Combine(_options.ReposFolder, ".darc-clone-debug", "graph"),
                    graph);

                if (_options.ForceCoherence)
                {
                    var newGraph = _cloneClient.CreateArtificiallyCoherentGraph(graph);

                    Logger.LogInformation(
                        $"Artificially forcing coherence. Node count {graph.Nodes.Count} -> " +
                        $"{newGraph.Nodes.Count} ({newGraph.Nodes.Count - graph.Nodes.Count}) ");

                    graph = newGraph;

                    WriteGraphDebugInfoToFiles(
                        Path.Combine(_options.ReposFolder, ".darc-clone-debug", "coherent-graph"),
                        graph);
                }

                var timeBeforeWorktrees = DateTimeOffset.UtcNow;

                await _cloneClient.CreateWorktreesAsync(
                    graph,
                    _options.ReposFolder);

                var timeWhenComplete = DateTimeOffset.UtcNow;

                Logger.LogInformation(
                    $"Done in {timeWhenComplete - timeBeforeGraph}. " +
                    $"Cloned and discovered graph in {timeBeforeWorktrees - timeBeforeGraph}. " +
                    $"Created worktrees in {timeWhenComplete - timeBeforeWorktrees}.");

                return Constants.SuccessCode;
            }
            catch (Exception exc)
            {
                Logger.LogError(exc, "Something failed while cloning.");
                return Constants.ErrorCode;
            }
        }

        private static void WriteGraphDebugInfoToFiles(string path, SourceBuildGraph graph)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            File.WriteAllText(
                $"{path}.dot",
                graph.ToGraphVizString());
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
    }
}
