// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    /// <summary>
    ///     An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    public sealed class AzureDevOpsTimeline : IServiceImplementation
    {
        private readonly ILogger<AzureDevOpsTimeline> _logger;
        private readonly IOptionsSnapshot<AzureDevOpsTimelineOptions> _options;
        private readonly ITimelineTelemetryRepository _timelineTelemetryRepository;
        private readonly IAzureDevOpsClient azureServer;
        private readonly ISystemClock _systemClock;

        public AzureDevOpsTimeline(
            ILogger<AzureDevOpsTimeline> logger,
            IOptionsSnapshot<AzureDevOpsTimelineOptions> options,
            ITimelineTelemetryRepository timelineTelemetryRepository,
            IAzureDevOpsClient azureDevopsClient,
            ISystemClock systemClock)
        {
            _logger = logger;
            _options = options;
            _timelineTelemetryRepository = timelineTelemetryRepository;
            azureServer = azureDevopsClient;
            _systemClock = systemClock;
        }

        public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            await Wait(_options.Value.InitialDelay, cancellationToken, TimeSpan.FromHours(1));

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunLoop(cancellationToken);
                }
                catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
                {
                    break;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "AzureDevOpsTimelineLoop failed with unhandled exception");
                }

                await Wait(_options.Value.Interval, cancellationToken, TimeSpan.FromHours(6));
            }

            return TimeSpan.Zero;
        }

        private Task Wait(string duration, CancellationToken cancellationToken, TimeSpan defaultTime)
        {
            if (!TimeSpan.TryParse(duration, out TimeSpan interval))
            {
                interval = defaultTime;
            }

            _logger.LogTrace($"Delaying for {interval:g}...");
            return Task.Delay(interval, cancellationToken);
        }

        private async Task RunLoop(CancellationToken cancellationToken)
        {
            // Fetch them again, we just waited an hour
            AzureDevOpsTimelineOptions options = _options.Value;

            if (!int.TryParse(options.BuildBatchSize, out int buildBatchSize) || buildBatchSize < 1)
            {
                buildBatchSize = 1000;
            }

            foreach (string project in options.AzureDevOpsProjects.Split(';'))
            {
                await RunProject(project, buildBatchSize, cancellationToken);
            }
        }

        public async Task RunProject(
            string project,
            int buildBatchSize,
            CancellationToken cancellationToken)
        {
            DateTimeOffset latest;
            DateTimeOffset? latestCandidate = await _timelineTelemetryRepository.GetLatestTimelineBuild(project);

            if (latestCandidate.HasValue)
            {
                latest = latestCandidate.Value;
            }
            else
            {
                latest = _systemClock.UtcNow.Subtract(TimeSpan.FromDays(30));
                _logger.LogWarning($"No previous time found, using {latest.LocalDateTime:O}");
            }

            _logger.LogInformation("Reading project {project}", project);
            Build[] builds = await GetBuildsAsync(azureServer, project, latest, buildBatchSize, cancellationToken);
            _logger.LogTrace("... found {builds} builds...", builds.Length);

            if (builds.Length == 0)
            {
                _logger.LogTrace("No work to do");
                return;
            }

            List<(int buildId, BuildRequestValidationResult validationResult)> validationResults = builds
                .SelectMany(
                    build => build.ValidationResults,
                    (build, validationResult) => (build.Id, validationResult))
                .ToList();

            _logger.LogTrace("Fetching timeline...");
            Dictionary<Build, Task<Timeline>> tasks = builds
                .ToDictionary(
                    build => build,
                    build => azureServer.GetTimelineAsync(project, build.Id, cancellationToken)
                );

            // for each timeline, look for previousAttempts.timelineId timelines
            // For each timeline, look at timeline.lastChangedOn for ingestion cutoff
            // timeline.lastChangedOn will be before build.finishTime

            await Task.WhenAll(tasks.Select(s => s.Value));

            // Look for retried timelines
            List<(Build build, Task<Timeline> timelineTask)> retriedTimelineTasks = new List<(Build, Task<Timeline>)>();

            foreach ((Build build, Task<Timeline> timelineTask) in tasks)
            {
                Timeline timeline = await timelineTask;

                if (timeline is null)
                {
                    _logger.LogDebug("No timeline found for buildid {buildid}", build.Id);
                    continue;
                }

                IEnumerable<string> additionalTimelineIds = timeline.Records
                    .Where(record => record.PreviousAttempts != null)
                    .SelectMany(record => record.PreviousAttempts)
                    .Where(attempt => attempt != null)
                    .Select(attempt => attempt.TimelineId)
                    .Distinct();

                retriedTimelineTasks.AddRange(
                    additionalTimelineIds.Select(
                        timelineId => (build, azureServer.GetTimelineAsync(project, build.Id, timelineId, cancellationToken))));
            }

            await Task.WhenAll(retriedTimelineTasks.Select(o => o.timelineTask));

            // Only get timelines happening after the cutoff
            List<(Build build, Task<Timeline> timeline)> allNewTimelines = new List<(Build build, Task<Timeline> timeline)>();
            allNewTimelines.AddRange(tasks.Select(t => (t.Key, t.Value)));
            allNewTimelines.AddRange(retriedTimelineTasks
                .Where(t => t.timelineTask.Result.LastChangedOn > latest));

            _logger.LogTrace("... finished timeline");

            var records = new List<AugmentedTimelineRecord>();
            var issues = new List<AugmentedTimelineIssue>();
            var augmentedBuilds = new List<AugmentedBuild>();

            _logger.LogTrace("Aggregating results...");
            foreach ((Build build, Task<Timeline> timelineTask) in allNewTimelines)
            {
                augmentedBuilds.Add(CreateAugmentedBuild(build));

                Timeline timeline = await timelineTask;
                if (timeline?.Records == null)
                {
                    continue;
                }

                var recordCache =
                    new Dictionary<string, AugmentedTimelineRecord>();
                var issueCache = new List<AugmentedTimelineIssue>();
                foreach (TimelineRecord record in timeline.Records)
                {
                    var augRecord = new AugmentedTimelineRecord(build.Id, timeline.Id, record);
                    recordCache.Add(record.Id, augRecord);
                    records.Add(augRecord);
                    if (record.Issues == null)
                    {
                        continue;
                    }

                    for (int iIssue = 0; iIssue < record.Issues.Length; iIssue++)
                    {
                        var augIssue =
                            new AugmentedTimelineIssue(build.Id, timeline.Id, record.Id, iIssue, record.Issues[iIssue]);
                        augIssue.Bucket = GetBucket(augIssue);
                        issueCache.Add(augIssue);
                        issues.Add(augIssue);
                    }
                }

                foreach (AugmentedTimelineRecord record in recordCache.Values)
                {
                    FillAugmentedOrder(record, recordCache);
                }

                foreach (AugmentedTimelineIssue issue in issueCache)
                {
                    if (recordCache.TryGetValue(issue.RecordId, out AugmentedTimelineRecord record))
                    {
                        issue.AugmentedIndex = record.AugmentedOrder + "." + issue.Index.ToString("D3");
                    }
                    else
                    {
                        issue.AugmentedIndex = "999." + issue.Index.ToString("D3");
                    }
                }
            }

            _logger.LogInformation("Saving TimelineBuilds...");
            await _timelineTelemetryRepository.WriteTimelineBuilds(augmentedBuilds);

            _logger.LogInformation("Saving TimelineValidationMessages...");
            await _timelineTelemetryRepository.WriteTimelineValidationMessages(validationResults);

            _logger.LogInformation("Saving TimelineRecords...");
            await _timelineTelemetryRepository.WriteTimelineRecords(records);

            _logger.LogInformation("Saving TimelineIssues...");
            await _timelineTelemetryRepository.WriteTimelineIssues(issues);
        }

        public AugmentedBuild CreateAugmentedBuild(Build build)
        {
            string targetBranch = "";

            try
            {
                if (build.Reason == "pullRequest")
                {
                    targetBranch = (string)JObject.Parse(build.Parameters)["system.pullRequest.targetBranch"];
                }
            }
            catch (JsonReaderException e)
            {
                _logger.LogError(e, "Unable to extract targetBranch from Build");
            }

            return new AugmentedBuild(build, targetBranch);
        }

        private static string GetBucket(AugmentedTimelineIssue augIssue)
        {
            string message = augIssue?.Raw?.Message;
            if (string.IsNullOrEmpty(message))
                return null;

            Match match = Regex.Match(message, @"\(NETCORE_ENGINEERING_TELEMETRY=([^)]*)\)");
            if (!match.Success)
                return null;

            return match.Groups[1].Value;
        }

        private static void FillAugmentedOrder(
            AugmentedTimelineRecord record,
            IReadOnlyDictionary<string, AugmentedTimelineRecord> recordCache)
        {
            if (!string.IsNullOrEmpty(record.AugmentedOrder))
            {
                return;
            }

            if (!string.IsNullOrEmpty(record.Raw.ParentId))
            {
                if (recordCache.TryGetValue(record.Raw.ParentId, out AugmentedTimelineRecord parent))
                {
                    FillAugmentedOrder(parent, recordCache);
                    record.AugmentedOrder = parent.AugmentedOrder + "." + record.Raw.Order.ToString("D3");
                    return;
                }

                record.AugmentedOrder = "999." + record.Raw.Order.ToString("D3");
                return;
            }

            record.AugmentedOrder = record.Raw.Order.ToString("D3");
        }

        private static async Task<Build[]> GetBuildsAsync(
            IAzureDevOpsClient azureServer,
            string project,
            DateTimeOffset minDateTime,
            int limit,
            CancellationToken cancellationToken)
        {
            return await azureServer.ListBuilds(project, cancellationToken, minDateTime, limit);
        }
    }
}
