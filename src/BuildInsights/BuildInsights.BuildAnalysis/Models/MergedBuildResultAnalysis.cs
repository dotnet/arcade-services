// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues.Models;

#nullable disable
namespace BuildInsights.BuildAnalysis.Models;

public class MergedBuildResultAnalysis
{
    public string CommitHash { get; set; }
    public ImmutableList<BuildResultAnalysis> CompletedPipelines { get; set; }
    public CheckResult OverallStatus { get; set; }
    public ImmutableList<Link> PendingBuildNames { get; set; }
    public ImmutableList<Link> FilteredPipelinesBuilds { get; set; }

    public ImmutableList<KnownIssue> CriticalIssues { get; set; }

    public MergedBuildResultAnalysis()
    {
    }

    public MergedBuildResultAnalysis(
        string commitHash,
        IEnumerable<BuildResultAnalysis> completedPipelines,
        CheckResult overallStatus,
        IEnumerable<Link> pendingBuildNames,
        IEnumerable<Link> filteredPipelinesBuilds,
        ImmutableList<KnownIssue> criticalIssues)
    {
        CommitHash = commitHash;
        CompletedPipelines = completedPipelines?.ToImmutableList() ?? [];
        PendingBuildNames = pendingBuildNames?.ToImmutableList() ?? [];
        FilteredPipelinesBuilds = filteredPipelinesBuilds?.ToImmutableList() ?? [];
        OverallStatus = overallStatus;
        CriticalIssues = criticalIssues ?? [];
    }
}
