// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using BuildInsights.Core;

namespace BuildInsights.BuildAnalysis;

/// <summary>
/// Analyzes build trends by comparing recent builds against historic baselines.
/// </summary>
public class BuildTrendAnalyzer
{
    private const double TrendDeltaThreshold = 0.1;

    /// <summary>
    /// Determines the trend direction by comparing recent build success rate to historic success rate.
    /// </summary>
    /// <param name="recentBuilds">Build results from the recent period.</param>
    /// <param name="historicBuilds">Build results from the historic baseline period.</param>
    public BuildTrend AnalyzeTrend(IReadOnlyList<BuildStatus> recentBuilds, IReadOnlyList<BuildStatus> historicBuilds)
    {
        if (recentBuilds.Count == 0 || historicBuilds.Count == 0)
        {
            return BuildTrend.Stable;
        }

        double recentSuccessRate = CalculateSuccessRate(recentBuilds);
        double historicSuccessRate = CalculateSuccessRate(historicBuilds);

        if (recentSuccessRate > historicSuccessRate + TrendDeltaThreshold)
        {
            return BuildTrend.Improving;
        }

        if (recentSuccessRate < historicSuccessRate - TrendDeltaThreshold)
        {
            return BuildTrend.Degrading;
        }

        return BuildTrend.Stable;
    }

    private static double CalculateSuccessRate(IReadOnlyList<BuildStatus> builds)
        => builds.Count(r => r == BuildStatus.Succeeded || r == BuildStatus.PartiallySucceeded) / (double)builds.Count;
}
