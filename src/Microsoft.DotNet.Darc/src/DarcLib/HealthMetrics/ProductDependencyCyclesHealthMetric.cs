// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.HealthMetrics
{
    /// <summary>
    ///     Calculate the existence or non-existence of product dependency cycles
    ///     of the product-only dependency graph starting at the specified repo+branch
    /// </summary>
    public class ProductDependencyCyclesHealthMetric : HealthMetric
    {
        public ProductDependencyCyclesHealthMetric(string repo, string branch, ILogger logger, IRemoteFactory remoteFactory)
            : base(logger, remoteFactory)
        {
            Repository = repo;
            Branch = branch;
        }

        public readonly string Repository;
        public readonly string Branch;
        public IEnumerable<IEnumerable<string>> Cycles;

        public override string MetricName => "Product Dependency Cycle Health";

        public override string MetricDescription => $"Product dependency cycle health for {Repository} @ {Branch}";

        public override async Task EvaluateAsync()
        {
            // Build the repository graph starting at the repo + branch and then look for cycles.
            // Build without build lookups, toolsets, etc. to minimize time. Toolset dependencies also break
            // product dependency cycles, so that eliminates some analysis

            DependencyGraphBuildOptions options = new DependencyGraphBuildOptions
            {
                EarlyBuildBreak = EarlyBreakOn.NoEarlyBreak,
                IncludeToolset = false,
                LookupBuilds = false,
                NodeDiff = NodeDiff.None,
                ComputeCyclePaths = true
            };

            // Evaluate and find out what the latest is on the branch
            var remote = await RemoteFactory.GetRemoteAsync(Repository, Logger);
            var commit = await remote.GetLatestCommitAsync(Repository, Branch);

            DependencyGraph graph =
                await DependencyGraph.BuildRemoteDependencyGraphAsync(RemoteFactory, Repository, commit, options, Logger);

            // Check to see whether there are any cycles.
            // Boil down the cycles into just the repositories involved
            Cycles = graph.Cycles.Select(cycle => cycle.Select(graphNode => graphNode.Repository));

            if (Cycles.Any())
            {
                Result = HealthResult.Failed;
            }
            else
            {
                Result = HealthResult.Passed;
            }
        }
    }
}
