// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kusto.Cloud.Platform.Utils;
using Kusto.Data.Common;
using Kusto.Data.Exceptions;
using Kusto.Data.Net.Client;
using Kusto.Ingest;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    /// <summary>
    ///     An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class AzureDevOpsTimeline : IServiceImplementation
    {
        private readonly Extensions.Logging.ILogger<AzureDevOpsTimeline> _logger;
        private readonly IOptionsSnapshot<AzureDevOpsTimelineOptions> _options;

        public AzureDevOpsTimeline(
            Extensions.Logging.ILogger<AzureDevOpsTimeline> logger,
            IOptionsSnapshot<AzureDevOpsTimelineOptions> options)
        {
            _logger = logger;
            _options = options;
        }

        public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            TraceSourceManager.SetTraceVerbosityForAll(TraceVerbosity.Fatal);

            await Wait(_options.Value.InitialDelay, cancellationToken, TimeSpan.FromHours(1));

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunLoop(cancellationToken);
                }
                catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
                {
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
            
            if (!int.TryParse(options.ParallelRequests, out int parallelRequests) || parallelRequests < 1)
            {
                parallelRequests = 5;
            }

            if (!int.TryParse(options.BuildBatchSize, out int buildBatchSize) || buildBatchSize < 1)
            {
                buildBatchSize = 1000;
            }

            _logger.LogTrace(
                "Opening connection to {organization} with {parallel} requests and access_token starting with '{token_sig}'",
                options.AzureDevOpsOrganization,
                parallelRequests,
                options.AzureDevOpsAccessToken.Substring(0, 2));

            var azureServer = new AzureDevOpsClient(
                options.AzureDevOpsUrl,
                options.AzureDevOpsOrganization,
                parallelRequests,
                options.AzureDevOpsAccessToken
            );



            foreach (string project in options.AzureDevOpsProjects.Split(';'))
            {
                await RunProject(azureServer, project, buildBatchSize, options, cancellationToken);
            }
        }

        private async Task RunProject(
            AzureDevOpsClient azureServer,
            string project,
            int buildBatchSize,
            AzureDevOpsTimelineOptions options,
            CancellationToken cancellationToken)
        {

            DateTimeOffset latest;
            try
            {
                using (ICslQueryProvider query =
                    KustoClientFactory.CreateCslQueryProvider(options.KustoQueryConnectionString))
                using (IDataReader result = await query.ExecuteQueryAsync(
                    options.KustoDatabase,
                    // This isn't use controlled, so I'm not worried about the Kusto injection
                    $"TimelineBuilds | where Project == '{project}' | summarize max(FinishTime)",
                    new ClientRequestProperties()
                ))
                {
                    if (!result.Read())
                    {
                        latest = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(30));
                        _logger.LogWarning($"No previous time found, using {latest.LocalDateTime:O}");
                    }
                    else
                    {
                        latest = result.GetDateTime(0);
                        _logger.LogInformation($"... fetched previous time of {latest.LocalDateTime:O}");
                    }
                }
            }
            catch(SemanticException e) when (e.SemanticErrors == "'where' operator: Failed to resolve column or scalar expression named 'Project'")
            {
                // The Project column isn't there, we probably reinitalized the tables
                latest = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(30));
                _logger.LogWarning($"No table column 'Project' found, assumed reinialization: using {latest.LocalDateTime:O}");
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

                IEnumerable<string> additionalTimelineIds = timeline.Records
                    .SelectMany(record => record.PreviousAttempts)
                    .Where(attempt => !(attempt is null))
                    .Select(attempt => attempt.TimelineId)
                    .Distinct(StringComparer.OrdinalIgnoreCase);

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
                string targetBranch = "";

                try
                {
                    if (build.Reason == "pullRequest")
                    {
                        targetBranch = (string) JObject.Parse(build.Parameters)["system.pullRequest.targetBranch"];
                    }
                }
                catch (JsonReaderException e)
                {
                    _logger.LogError(e.ToString());
                }

                augmentedBuilds.Add(new AugmentedBuild(build, targetBranch));

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

            if (string.IsNullOrEmpty(options.KustoIngestConnectionString))
            {
                _logger.LogError("No KustoIngestConnectionString set");
                return;
            }

            IKustoIngestClient ingest =
                KustoIngestFactory.CreateQueuedIngestClient(options.KustoIngestConnectionString);

            _logger.LogInformation("Saving TimelineBuilds...");
            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                ingest,
                options.KustoDatabase,
                "TimelineBuilds",
                _logger,
                augmentedBuilds,
                b => new[]
                {
                    new KustoValue("BuildId", b.Build.Id, KustoDataType.Int),
                    new KustoValue("Status", b.Build.Status, KustoDataType.String),
                    new KustoValue("Result", b.Build.Result, KustoDataType.String),
                    new KustoValue("Repository", b.Build.Repository?.Name ?? b.Build.Repository?.Id, KustoDataType.String),
                    new KustoValue("Reason", b.Build.Reason, KustoDataType.String),
                    new KustoValue("BuildNumber", b.Build.BuildNumber, KustoDataType.String),
                    new KustoValue("QueueTime", b.Build.QueueTime, KustoDataType.DateTime),
                    new KustoValue("StartTime", b.Build.StartTime, KustoDataType.DateTime),
                    new KustoValue("FinishTime", b.Build.FinishTime, KustoDataType.DateTime),
                    new KustoValue("Project", b.Build.Project?.Name, KustoDataType.String),
                    new KustoValue("DefinitionId", b.Build.Definition?.Id.ToString(), KustoDataType.String),
                    new KustoValue("Definition", $"{b.Build.Definition?.Path}\\{b.Build.Definition?.Name}", KustoDataType.String),
                    new KustoValue("SourceBranch", GitHelpers.NormalizeBranchName(b.Build.SourceBranch), KustoDataType.String),
                    new KustoValue("TargetBranch", GitHelpers.NormalizeBranchName(b.TargetBranch), KustoDataType.String),
                });

            _logger.LogInformation("Saving TimelineValidationMessages...");
            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                ingest,
                options.KustoDatabase,
                "TimelineIssues",
                _logger,
                validationResults,
                b => new[]
                {
                    new KustoValue("BuildId", b.buildId, KustoDataType.Int),
                    new KustoValue("RecordId", null, KustoDataType.String),
                    new KustoValue("Index", null, KustoDataType.Int),
                    new KustoValue("Path", null, KustoDataType.String),
                    new KustoValue("Type", b.validationResult.Result, KustoDataType.String),
                    new KustoValue("Category", "ValidationResult", KustoDataType.String),
                    new KustoValue("Message", b.validationResult.Message, KustoDataType.String),
                    new KustoValue("Bucket", "ValidationResult", KustoDataType.String),
                });

            _logger.LogInformation("Saving TimelineRecords...");
            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                ingest,
                options.KustoDatabase,
                "TimelineRecords",
                _logger,
                records,
                b => new[]
                {
                    new KustoValue("BuildId", b.BuildId, KustoDataType.Int),
                    new KustoValue("RecordId", b.Raw.Id, KustoDataType.String),
                    new KustoValue("TimelineId", b.TimelineId, KustoDataType.String),
                    new KustoValue("Order", b.Raw.Order, KustoDataType.Int),
                    new KustoValue("Path", b.AugmentedOrder, KustoDataType.String),
                    new KustoValue("ParentId", b.Raw.ParentId, KustoDataType.String),
                    new KustoValue("Name", b.Raw.Name, KustoDataType.String),
                    new KustoValue("StartTime", b.Raw.StartTime, KustoDataType.DateTime),
                    new KustoValue("FinishTime", b.Raw.FinishTime, KustoDataType.DateTime),
                    new KustoValue("Result", b.Raw.Result, KustoDataType.String),
                    new KustoValue("ResultCode", b.Raw.ResultCode, KustoDataType.String),
                    new KustoValue("ChangeId", b.Raw.ChangeId, KustoDataType.Int),
                    new KustoValue("LastModified", b.Raw.LastModified, KustoDataType.DateTime),
                    new KustoValue("WorkerName", b.Raw.WorkerName, KustoDataType.String),
                    new KustoValue("Details", b.Raw.Details?.Url, KustoDataType.String),
                    new KustoValue("ErrorCount", b.Raw.ErrorCount, KustoDataType.Int),
                    new KustoValue("WarningCount", b.Raw.WarningCount, KustoDataType.Int),
                    new KustoValue("Url", b.Raw.Url, KustoDataType.String),
                    new KustoValue("LogId", b.Raw.Log?.Id, KustoDataType.Int),
                    new KustoValue("LogUri", b.Raw.Log?.Url, KustoDataType.String),
                    new KustoValue("TaskId", b.Raw.Task?.Id, KustoDataType.Int),
                    new KustoValue("TaskName", b.Raw.Task?.Name, KustoDataType.String),
                    new KustoValue("TaskVersion", b.Raw.Task?.Version, KustoDataType.String),
                    new KustoValue("Attempt", b.Raw.Attempt, KustoDataType.Int),
                });

            _logger.LogInformation("Saving TimelineIssues...");
            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                ingest,
                options.KustoDatabase,
                "TimelineIssues",
                _logger,
                issues,
                b => new[]
                {
                    new KustoValue("BuildId", b.BuildId, KustoDataType.Int),
                    new KustoValue("RecordId", b.RecordId, KustoDataType.String),
                    new KustoValue("TimelineId", b.TimelineId, KustoDataType.String),
                    new KustoValue("Index", b.Index, KustoDataType.Int),
                    new KustoValue("Path", b.AugmentedIndex, KustoDataType.String),
                    new KustoValue("Type", b.Raw.Type, KustoDataType.String),
                    new KustoValue("Category", b.Raw.Category, KustoDataType.String),
                    new KustoValue("Message", b.Raw.Message, KustoDataType.String),
                    new KustoValue("Bucket", b.Bucket, KustoDataType.String),
                });
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
            AzureDevOpsClient azureServer,
            string project,
            DateTimeOffset minDateTime,
            int limit,
            CancellationToken cancellationToken)
        {
            return await azureServer.ListBuilds(project, cancellationToken, minDateTime, limit);
        }

        private class AugmentedBuild
        {
            public AugmentedBuild(Build build, string targetBranch)
            {
                Build = build;
                TargetBranch = targetBranch;
            }

            public Build Build { get; }
            public string TargetBranch { get; }
        }

        private class AugmentedTimelineRecord
        {
            public AugmentedTimelineRecord(int buildId, string timelineId, TimelineRecord raw)
            {
                BuildId = buildId;
                TimelineId = timelineId;
                Raw = raw;
            }

            public int BuildId { get; }
            public string TimelineId { get; }
            public TimelineRecord Raw { get; }
            public string AugmentedOrder { get; set; }
        }

        private class AugmentedTimelineIssue
        {
            public AugmentedTimelineIssue(int buildId, string timelineId, string recordId, int index, TimelineIssue raw)
            {
                BuildId = buildId;
                TimelineId = timelineId;
                RecordId = recordId;
                Index = index;
                Raw = raw;
            }

            public int BuildId { get; }
            public string TimelineId { get; }
            public string RecordId { get; }
            public int Index { get; }
            public TimelineIssue Raw { get; }
            public string AugmentedIndex { get; set; }
            public string Bucket { get; set; }
        }
    }
}
