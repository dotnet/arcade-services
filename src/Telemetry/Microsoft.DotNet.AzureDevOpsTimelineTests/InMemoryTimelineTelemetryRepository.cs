// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests;

/// <summary>
/// Holds TimelineTelemetry data objects in-memory
/// </summary>
public class InMemoryTimelineTelemetryRepository : ITimelineTelemetryRepository
{
    public List<AugmentedBuild> TimelineBuilds { get; } = new List<AugmentedBuild>();
    public List<AugmentedTimelineIssue> TimelineIssues { get; } = new List<AugmentedTimelineIssue>();
    public List<AugmentedTimelineRecord> TimelineRecords { get; } = new List<AugmentedTimelineRecord>();
    public List<(int buildId, BuildRequestValidationResult validationResult)> TimelineValidationMessages { get; } = new List<(int buildId, BuildRequestValidationResult validationResult)>();

    private readonly List<(string project, string organization, DateTimeOffset latestTime)> _latestTimes;

    public InMemoryTimelineTelemetryRepository()
    {
        _latestTimes = new List<(string project, string organization, DateTimeOffset latestTime)>();
    }

    public InMemoryTimelineTelemetryRepository(List<(string project, string organization, DateTimeOffset latestTime)> latestTimes)
    {
        _latestTimes = latestTimes;
    }

    public Task<DateTimeOffset?> GetLatestTimelineBuild(AzureDevOpsProject project)
    {
        DateTimeOffset candidate = _latestTimes.FirstOrDefault(latest => latest.project == project.Project && latest.organization == project.Organization).latestTime;
        if (candidate == default)
            return Task.FromResult<DateTimeOffset?>(null);
        else
            return Task.FromResult<DateTimeOffset?>(candidate);
    }

    public Task WriteTimelineBuilds(IEnumerable<AugmentedBuild> augmentedBuilds, string organization)
    {
        TimelineBuilds.AddRange(augmentedBuilds);
        return Task.CompletedTask;
    }

    public Task WriteTimelineIssues(IEnumerable<AugmentedTimelineIssue> issues)
    {
        TimelineIssues.AddRange(issues);
        return Task.CompletedTask;
    }

    public Task WriteTimelineRecords(IEnumerable<AugmentedTimelineRecord> records)
    {
        TimelineRecords.AddRange(records);
        return Task.CompletedTask;
    }

    public Task WriteTimelineValidationMessages(IEnumerable<(int buildId, BuildRequestValidationResult validationResult)> validationResults)
    {
        TimelineValidationMessages.AddRange(validationResults);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
