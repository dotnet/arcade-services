// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Models.Darc;
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
    private readonly IBasicBarClient _barClient;
    private readonly ILogger _logger;

    public ProductDependencyCyclesHealthMetric(
        string repo,
        string branch,
        IRemoteFactory remoteFactory,
        IBasicBarClient barClient,
        ILogger logger)
    {
        _repository = repo;
        _branch = branch;
        _remoteFactory = remoteFactory;
        _barClient = barClient;
        _logger = logger;
    }

    public IEnumerable<IEnumerable<string>> Cycles { get; private set; }

    public override string MetricName => "Product Dependency Cycle Health";

    public override string MetricDescription => $"Product dependency cycle health for {_repository} @ {_branch}";

    public override async Task EvaluateAsync()
    {
        // Build the repository graph starting at the repo + branch and then look for cycles.
        // Build without build lookups, toolsets, etc. to minimize time. Toolset dependencies also break
        // product dependency cycles, so that eliminates some analysis

        var options = new DependencyGraphBuildOptions
        {
            IncludeToolset = false,
            LookupBuilds = false,
            NodeDiff = NodeDiff.None,
            ComputeCyclePaths = true
        };

        // Evaluate and find out what the latest is on the branch
        var remote = await _remoteFactory.GetRemoteAsync(_repository, _logger);
        var commit = await remote.GetLatestCommitAsync(_repository, _branch);

        if (commit == null)
        {
            // If there were no commits, then there can be no cycles. This would be typical of newly
            // created branches.
            Result = HealthResult.Passed;
            Cycles = [];
            return;
        }

        DependencyGraph graph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
            _remoteFactory,
            _barClient,
            _repository,
            commit,
            options,
            _logger);

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
