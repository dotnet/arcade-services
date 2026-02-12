// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class FailureRate {
    public FailureRate() { }

    public int FailedRuns { get; set; }
    public int TotalRuns { get; set; }
    public double? PercentageOfFailure => TotalRuns > 0 ? FailedRuns / (double)TotalRuns : null;

    public string PercentOfFailureToLinkString => string.Format("[{0} failure rate]", PercentageOfFailure?.ToString("P")) ?? null;
    
    /// <summary>
    /// Since when we are counting the total runs / failed runs
    /// </summary>
    public DateTimeOffset DateOfRate { get; set; }
}
