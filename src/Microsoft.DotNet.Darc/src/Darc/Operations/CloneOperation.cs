// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Actions.Clone;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
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
