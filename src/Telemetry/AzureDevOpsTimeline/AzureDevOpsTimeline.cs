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
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.DotNet.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Maestro.Data;
using MaestroBuild = Maestro.Data.Models.Build;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    /// <summary>
    ///     An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class AzureDevOpsTimeline : IServiceImplementation
    {
        private readonly Extensions.Logging.ILogger<AzureDevOpsTimeline> _logger;
        private readonly IOptionsSnapshot<AzureDevOpsTimelineOptions> _options;
        private readonly BuildAssetRegistryContext _context;

        public AzureDevOpsTimeline(
            Extensions.Logging.ILogger<AzureDevOpsTimeline> logger,
            IOptionsSnapshot<AzureDevOpsTimelineOptions> options,
            BuildAssetRegistryContext context)
        {
            _logger = logger;
            _options = options;
            _context = context;
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

            await Task.WhenAll(tasks.Select(s => s.Value));
            _logger.LogTrace("... finished timeline");

            var records = new List<AugmentedTimelineRecord>();
            var issues = new List<AugmentedTimelineIssue>();

            _logger.LogTrace("Aggregating results...");
            foreach ((Build build, Task<Timeline> timelineTask) in tasks)
            {
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
                    var augRecord = new AugmentedTimelineRecord(build.Id, record);
                    recordCache.Add(record.Id, augRecord);
                    records.Add(augRecord);
                    if (record.Issues == null)
                    {
                        continue;
                    }

                    for (int iIssue = 0; iIssue < record.Issues.Length; iIssue++)
                    {
                        var augIssue =
                            new AugmentedTimelineIssue(build.Id, record.Id, iIssue, record.Issues[iIssue]);
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
                builds,
                b => new[]
                {
                    new KustoValue("BuildId", b.Id.ToString(), KustoDataTypes.Int),
                    new KustoValue("Status", b.Status, KustoDataTypes.String),
                    new KustoValue("Result", b.Result, KustoDataTypes.String),
                    new KustoValue("Repository", b.Repository?.Name ?? b.Repository?.Id, KustoDataTypes.String),
                    new KustoValue("Reason", b.Reason, KustoDataTypes.String),
                    new KustoValue("BuildNumber", b.BuildNumber, KustoDataTypes.String),
                    new KustoValue("QueueTime", b.QueueTime, KustoDataTypes.DateTime),
                    new KustoValue("StartTime", b.StartTime, KustoDataTypes.DateTime),
                    new KustoValue("FinishTime", b.FinishTime, KustoDataTypes.DateTime),
                    new KustoValue("Project", b.Project?.Name, KustoDataTypes.String),
                    new KustoValue("DefinitionId", b.Definition?.Id.ToString(), KustoDataTypes.String),
                    new KustoValue("Definition", $"{b.Definition?.Path}\\{b.Definition?.Name}", KustoDataTypes.String),
                    new KustoValue("SourceBranch", b.SourceBranch, KustoDataTypes.String),
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
                    new KustoValue("BuildId", b.buildId.ToString(), KustoDataTypes.Int),
                    new KustoValue("RecordId", null, KustoDataTypes.String),
                    new KustoValue("Index", null, KustoDataTypes.Int),
                    new KustoValue("Path", null, KustoDataTypes.String),
                    new KustoValue("Type", b.validationResult.Result, KustoDataTypes.String),
                    new KustoValue("Category", "ValidationResult", KustoDataTypes.String),
                    new KustoValue("Message", b.validationResult.Message, KustoDataTypes.String),
                    new KustoValue("Bucket", "ValidationResult", KustoDataTypes.String),
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
                    new KustoValue("BuildId", b.BuildId.ToString(), KustoDataTypes.Int),
                    new KustoValue("RecordId", b.Raw.Id, KustoDataTypes.String),
                    new KustoValue("Order", b.Raw.Order.ToString(), KustoDataTypes.Int),
                    new KustoValue("Path", b.AugmentedOrder, KustoDataTypes.String),
                    new KustoValue("ParentId", b.Raw.ParentId, KustoDataTypes.String),
                    new KustoValue("Name", b.Raw.Name, KustoDataTypes.String),
                    new KustoValue("StartTime", b.Raw.StartTime, KustoDataTypes.DateTime),
                    new KustoValue("FinishTime", b.Raw.FinishTime, KustoDataTypes.DateTime),
                    new KustoValue("Result", b.Raw.Result, KustoDataTypes.String),
                    new KustoValue("ResultCode", b.Raw.ResultCode, KustoDataTypes.String),
                    new KustoValue("ChangeId", b.Raw.ChangeId.ToString(), KustoDataTypes.Int),
                    new KustoValue("LastModified", b.Raw.LastModified, KustoDataTypes.DateTime),
                    new KustoValue("WorkerName", b.Raw.WorkerName, KustoDataTypes.String),
                    new KustoValue("Details", b.Raw.Details?.Url, KustoDataTypes.String),
                    new KustoValue("ErrorCount", b.Raw.ErrorCount.ToString(), KustoDataTypes.Int),
                    new KustoValue("WarningCount", b.Raw.WarningCount.ToString(), KustoDataTypes.Int),
                    new KustoValue("Url", b.Raw.Url, KustoDataTypes.String),
                    new KustoValue("LogId", b.Raw.Log?.Id.ToString(), KustoDataTypes.Int),
                    new KustoValue("LogUri", b.Raw.Log?.Url, KustoDataTypes.String),
                    new KustoValue("TaskId", b.Raw.Task?.Id, KustoDataTypes.Int),
                    new KustoValue("TaskName", b.Raw.Task?.Name, KustoDataTypes.String),
                    new KustoValue("TaskVersion", b.Raw.Task?.Version, KustoDataTypes.String),
                    new KustoValue("Attempt", b.Raw.Attempt.ToString(), KustoDataTypes.Int),
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
                    new KustoValue("BuildId", b.BuildId.ToString(), KustoDataTypes.Int),
                    new KustoValue("RecordId", b.RecordId, KustoDataTypes.String),
                    new KustoValue("Index", b.Index.ToString(), KustoDataTypes.Int),
                    new KustoValue("Path", b.AugmentedIndex, KustoDataTypes.String),
                    new KustoValue("Type", b.Raw.Type, KustoDataTypes.String),
                    new KustoValue("Category", b.Raw.Category, KustoDataTypes.String),
                    new KustoValue("Message", b.Raw.Message, KustoDataTypes.String),
                    new KustoValue("Bucket", b.Bucket, KustoDataTypes.String),
                });

            List<BuildPath> longestBuildPaths = new List<BuildPath>();

            foreach (var build in builds)
            {
                MaestroBuild root = _context.Builds.Where(b => b.AzureDevOpsBuildId == build.Id).FirstOrDefault();
                if (root != null)
                {
                    longestBuildPaths.Add(await GetLongestBuildPath(root, new List<string>(), builds, options));
                }
            }

            BuildPath[] buildPaths = longestBuildPaths.ToArray();
            //Add this thing to Kusto
            _logger.LogInformation("Saving LongestBuildPath...");
            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                ingest,
                options.KustoDatabase,
                "LongestBuildPath",
                _logger,
                buildPaths,
                b => new[]
                {
                    new KustoValue("BuildId", b.BuildId.ToString(), KustoDataTypes.Int),
                    new KustoValue("LongestBuildPath", b.Path, KustoDataTypes.String),
                    new KustoValue("LongestBuildPathTime", b.Time.ToString(), KustoDataTypes.Decimal),
                });
        }

        private async Task<BuildPath> GetLongestBuildPath(MaestroBuild build, List<string> visitedRepositories, Build[] builds, AzureDevOpsTimelineOptions options)
        {
            int buildId = 0;
            double buildTime = 0;
            string repository = "";

            if (build == null)
            {
                return new BuildPath(buildId, repository, buildTime);
            }

            // Lookup Kusto build. First check builds, then check the Kusto database.
            Build kustoBuild = builds.FirstOrDefault(b => b.Id == build.AzureDevOpsBuildId);

            if (kustoBuild == null)
            {
                // Build is not in the builds list, so we need to look this build up in Kusto
                try
                {
                    using (ICslQueryProvider query =
                        KustoClientFactory.CreateCslQueryProvider(options.KustoQueryConnectionString))
                    using (IDataReader result = await query.ExecuteQueryAsync(
                        options.KustoDatabase,
                        // This isn't use controlled, so I'm not worried about the Kusto injection
                        $"TimelineBuilds | where BuildId == '{build.AzureDevOpsBuildId}' | project BuildId, Repository, StartTime, FinishTime",
                        new ClientRequestProperties()
                    ))
                    {
                        if (!result.Read())
                        {
                            if (build.AzureDevOpsBuildId != null)
                            {
                                buildId = (int) build.AzureDevOpsBuildId;
                            }
                            repository = build.GitHubRepository;
                            _logger.LogWarning($"No build found, assuming build time of {buildTime}, repository {repository} for build id of {buildId}");
                        }
                        else
                        {
                            buildId = result.GetInt32(0);
                            repository = result.GetString(1);
                            buildTime = (result.GetDateTime(3) - result.GetDateTime(2)).TotalMinutes;
                            _logger.LogInformation($"... fetched build time of {buildTime} and repository {repository} for build id {buildId}");
                        }
                    }
                }
                catch(SemanticException)
                {
                    // One of the column aren't there, we probably reinitalized the tables
                    if (build.AzureDevOpsBuildId != null)
                    {
                        buildId = (int) build.AzureDevOpsBuildId;
                    }
                    repository = build.GitHubRepository;

                    _logger.LogWarning($"One or more table columns not found, assumed reinialization: using build time of {buildTime}," +
                                        $" repository {repository} for build id of {buildId}");
                }
            }
            else
            {
                buildId = kustoBuild.Id;
                buildTime = (Convert.ToDateTime(kustoBuild.FinishTime) - Convert.ToDateTime(kustoBuild.StartTime)).TotalMinutes;
                repository = $"{kustoBuild.Repository.Name}";
            }

            // Look up the build in BAR
            var dependencies = _context.BuildDependencies.Where(b => b.BuildId == build.Id && b.IsProduct == true);

            // If we have already visited this repository (ie we have hit a cycle) or this node does not have
            // any dependencies, stop searching.
            if (visitedRepositories.Contains(repository) || dependencies.Count() == 0)
            {
                return new BuildPath(buildId, repository, buildTime);
            }

            // We haven't seen this repository yet, so add it to the list of visited repositories
            List<string> currentVisitedRepositories = visitedRepositories;
            currentVisitedRepositories.Add(repository);

            // Recursively find the longest build path of each of this node's dependencies.
            BuildPath currentLongest = new BuildPath(buildId, repository, 0);

            foreach (var dep in dependencies)
            {
                MaestroBuild dependency = _context.Builds.Where(b => b.Id == dep.DependentBuildId).FirstOrDefault();
                BuildPath depLongest = await GetLongestBuildPath(dependency, currentVisitedRepositories, builds, options);

                if (depLongest.Time + dep.TimeToInclusionInMinutes > currentLongest.Time)
                {
                    currentLongest.Time = depLongest.Time + dep.TimeToInclusionInMinutes;
                    currentLongest.Path = $"{repository} {depLongest.Path}";
                }
            }

            return currentLongest;
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

        private class AugmentedTimelineRecord
        {
            public AugmentedTimelineRecord(int buildId, TimelineRecord raw)
            {
                BuildId = buildId;
                Raw = raw;
            }

            public int BuildId { get; }
            public TimelineRecord Raw { get; }
            public string AugmentedOrder { get; set; }
        }

        private class AugmentedTimelineIssue
        {
            public AugmentedTimelineIssue(int buildId, string recordId, int index, Issue raw)
            {
                BuildId = buildId;
                RecordId = recordId;
                Index = index;
                Raw = raw;
            }

            public int BuildId { get; }
            public string RecordId { get; }
            public int Index { get; }
            public Issue Raw { get; }
            public string AugmentedIndex { get; set; }
            public string Bucket { get; set; }
        }

        private class BuildPath
        {
            public BuildPath()
            {
                BuildId = 0;
                Path = "";
                Time = 0;
            }

            public BuildPath(int buildId, string buildPath, double buildPathTime)
            {
                BuildId = buildId;
                Path = buildPath;
                Time = buildPathTime;
            }

            public int BuildId { get; set; }
            public string Path { get; set; }
            public double Time { get; set; }
        }
    }
}
