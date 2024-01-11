// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib.HealthMetrics;

/// <summary>
///     Calculate the existence or non-existence of product dependency cycles
///     of the product-only dependency graph starting at the specified repo+branch
/// </summary>
public class ProductDependencyCyclesHealthMetric : HealthMetric
{
    private readonly string _repository;
    private readonly string _branch;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IBarApiClientFactory _barApiClientFactory;
    private IEnumerable<IEnumerable<string>> _cycles;

    public ProductDependencyCyclesHealthMetric(
        string repo,
        string branch,
        ILogger logger,
        IRemoteFactory remoteFactory,
        IBarApiClientFactory barApiClientFactory)
        : base(logger, remoteFactory)
    {
        _repository = repo;
        _branch = branch;
        _remoteFactory = remoteFactory;
        _barApiClientFactory = barApiClientFactory;
    }

    public override string MetricName => "Product Dependency Cycle Health";

    public override string MetricDescription => $"Product dependency cycle health for {_repository} @ {_branch}";

    public override async Task EvaluateAsync()
    {
        // Build the repository graph starting at the repo + branch and then look for cycles.
        // Build without build lookups, toolsets, etc. to minimize time. Toolset dependencies also break
        // product dependency cycles, so that eliminates some analysis

        DependencyGraphBuildOptions options = new DependencyGraphBuildOptions
        {
            IncludeToolset = false,
            LookupBuilds = false,
            NodeDiff = NodeDiff.None,
            ComputeCyclePaths = true
        };

        // Evaluate and find out what the latest is on the branch
        var remote = await _remoteFactory.GetRemoteAsync(_repository, Logger);
        var commit = await remote.GetLatestCommitAsync(_repository, _branch);

        if (commit == null)
        {
            // If there were no commits, then there can be no cycles. This would be typical of newly
            // created branches.
            Result = HealthResult.Passed;
            _cycles = new List<List<string>>();
            return;
        }

        DependencyGraph graph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
            _remoteFactory,
            _barApiClientFactory,
            _repository,
            commit,
            options,
            Logger);

        // Check to see whether there are any cycles.
        // Boil down the cycles into just the repositories involved
        _cycles = graph.Cycles.Select(cycle => cycle.Select(graphNode => graphNode.Repository));

        if (_cycles.Any())
        {
            Result = HealthResult.Failed;
        }
        else
        {
            Result = HealthResult.Passed;
        }
    }
}
