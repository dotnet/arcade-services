// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using BuildInsights.Core;

namespace BuildInsights.BuildAnalysis;

/// <summary>
/// Evaluates the health of a repository's builds based on a configurable success rate threshold.
/// </summary>
public class BuildHealthEvaluator : IBuildHealthEvaluator
{
    private readonly double _minimumSuccessRate;

    /// <param name="minimumSuccessRate">Minimum success rate (0.0–1.0) to consider builds healthy. Defaults to 0.8.</param>
    public BuildHealthEvaluator(double minimumSuccessRate = 0.8)
    {
        _minimumSuccessRate = minimumSuccessRate;
    }

    public BuildHealthSummary EvaluateHealth(string repositoryUrl, string branch, IReadOnlyList<BuildStatus> buildResults)
    {
        int total = buildResults.Count;
        int succeeded = buildResults.Count(r => r == BuildStatus.Succeeded || r == BuildStatus.PartiallySucceeded);
        int failed = buildResults.Count(r => r == BuildStatus.Failed || r == BuildStatus.Cancelled);

        return new BuildHealthSummary(repositoryUrl, branch, total, succeeded, failed);
    }

    public bool IsHealthy(BuildHealthSummary summary) => summary.SuccessRate >= _minimumSuccessRate;
}
