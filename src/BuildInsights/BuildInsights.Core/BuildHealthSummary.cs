// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.Core;

/// <summary>
/// Summarizes the build health for a specific repository branch over a set of builds.
/// </summary>
/// <param name="RepositoryUrl">URL of the repository.</param>
/// <param name="Branch">Branch name.</param>
/// <param name="TotalBuilds">Total number of builds evaluated.</param>
/// <param name="SuccessfulBuilds">Number of builds that succeeded.</param>
/// <param name="FailedBuilds">Number of builds that failed.</param>
public record BuildHealthSummary(
    string RepositoryUrl,
    string Branch,
    int TotalBuilds,
    int SuccessfulBuilds,
    int FailedBuilds)
{
    public double SuccessRate => TotalBuilds > 0 ? (double)SuccessfulBuilds / TotalBuilds : 0.0;
}
