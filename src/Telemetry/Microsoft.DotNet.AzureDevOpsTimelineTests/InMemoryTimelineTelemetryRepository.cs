using Microsoft.DotNet.Internal.AzureDevOps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    /// <summary>
    /// Holds TimelineTelemetry data objects in-memory
    /// </summary>
    public class InMemoryTimelineTelemetryRepository : ITimelineTelemetryRepository
    {
        public List<AugmentedBuild> TimelineBuilds { get; } = new List<AugmentedBuild>();
        public List<AugmentedTimelineIssue> TimelineIssues { get; } = new List<AugmentedTimelineIssue>();
        public List<AugmentedTimelineRecord> TimelineRecords { get; } = new List<AugmentedTimelineRecord>();
        public List<(int buildId, BuildRequestValidationResult validationResult)> TimelineValidationMessages { get; } = new List<(int buildId, BuildRequestValidationResult validationResult)>();

        private readonly List<(string project, DateTimeOffset latestTime)> _latestTimes;

        public InMemoryTimelineTelemetryRepository()
        {
            _latestTimes = new List<(string project, DateTimeOffset latestTime)>();
        }

        public InMemoryTimelineTelemetryRepository(List<(string project, DateTimeOffset latestTime)> latestTimes)
        {
            _latestTimes = latestTimes;
        }

        public Task<DateTimeOffset?> GetLatestTimelineBuild(string project)
        {
            DateTimeOffset candidate = _latestTimes.FirstOrDefault(latest => latest.project == project).latestTime;
            if (candidate == default)
                return Task.FromResult<DateTimeOffset?>(null);
            else
                return Task.FromResult<DateTimeOffset?>(candidate);
        }

        public Task WriteTimelineBuilds(IEnumerable<AugmentedBuild> augmentedBuilds)
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

}
