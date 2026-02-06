// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Internal.Helix.GitHub.Models;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class MergedBuildResultAnalysis
    {
        public string CommitHash { get; set; }
        public ImmutableList<BuildResultAnalysis> CompletedPipelines { get; set; }
        public CheckResult OverallStatus { get; set; }
        public ImmutableList<Link> PendingBuildNames { get; set; }
        public ImmutableList<Link> FilteredPipelinesBuilds { get; set; }

        public ImmutableList<KnownIssue> CriticalIssues { get; set; }

        public MergedBuildResultAnalysis() { }

        public MergedBuildResultAnalysis(
            string commitHash,
            IEnumerable<BuildResultAnalysis> completedPipelines,
            CheckResult overallStatus,
            IEnumerable<Link> pendingBuildNames,
            IEnumerable<Link> filteredPipelinesBuilds,
            ImmutableList<KnownIssue> criticalIssues)
        {
            CommitHash = commitHash;
            CompletedPipelines = completedPipelines?.ToImmutableList() ?? ImmutableList<BuildResultAnalysis>.Empty;
            PendingBuildNames = pendingBuildNames?.ToImmutableList() ?? ImmutableList<Link>.Empty;
            FilteredPipelinesBuilds = filteredPipelinesBuilds?.ToImmutableList() ?? ImmutableList<Link>.Empty;
            OverallStatus = overallStatus;
            CriticalIssues = criticalIssues ?? ImmutableList<KnownIssue>.Empty;
        }
    }
}
