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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Internal.DependencyInjection;

namespace Microsoft.DotNet.AzureDevOpsTimeline;

/// <summary>
///     An instance of this class is created for each service instance by the Service Fabric runtime.
/// </summary>
public sealed class AzureDevOpsTimeline : IServiceImplementation
{
    private readonly ILogger<AzureDevOpsTimeline> _logger;
    private readonly AzureDevOpsTimelineOptions _options;
    private readonly ITimelineTelemetryRepository _timelineTelemetryRepository;
    private readonly IClientFactory<IAzureDevOpsClient> _azureDevopsClientFactory;
    private readonly ISystemClock _systemClock;
    private readonly IBuildLogScraper _buildLogScraper;

    public AzureDevOpsTimeline(
        ILogger<AzureDevOpsTimeline> logger,
        IOptionsSnapshot<AzureDevOpsTimelineOptions> options,
        ITimelineTelemetryRepository timelineTelemetryRepository,
        IClientFactory<IAzureDevOpsClient> azureDevopsClientFactory,
        ISystemClock systemClock,
        IBuildLogScraper buildLogScraper)
    {
        _logger = logger;
        _options = options.Value;
        _timelineTelemetryRepository = timelineTelemetryRepository;
        _azureDevopsClientFactory = azureDevopsClientFactory;
        _systemClock = systemClock;
        _buildLogScraper = buildLogScraper;
    }

    public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.Parse(_options.InitialDelay), cancellationToken);
            await RunLoop(cancellationToken);
        }
        catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "AzureDevOpsTimeline failed with unhandled exception");
        }

        return TimeSpan.Parse(_options.Interval);
    }

    private async Task RunLoop(CancellationToken cancellationToken)
    {
        if (!int.TryParse(_options.BuildBatchSize, out int buildBatchSize) || buildBatchSize < 1)
        {
            buildBatchSize = 1000;
        }

        foreach (var instance in _options.Projects)
        {
            await RunProject(instance, buildBatchSize, cancellationToken);
        }
    }

    public async Task RunProject(
        AzureDevOpsProject project,
        int buildBatchSize,
        CancellationToken cancellationToken)
    {
        using var _ = _logger.BeginScope("Reading project {Organization}/{Project}", project.Organization, project.Project);
                
        DateTimeOffset latest;
        DateTimeOffset? latestCandidate = await _timelineTelemetryRepository.GetLatestTimelineBuild(project);

        if (latestCandidate.HasValue)
        {
            latest = latestCandidate.Value;
        }
        else
        {
            latest = _systemClock.UtcNow.Subtract(TimeSpan.FromDays(30));
            _logger.LogWarning("No previous time found, using {StartTime}", latest.LocalDateTime);
        }

        using var azDoClientRef = _azureDevopsClientFactory.GetClient(project.Organization);
        var azDoClient = azDoClientRef.Value;

        Build[] builds = await GetBuildsAsync(azDoClient, project.Project, latest, buildBatchSize, cancellationToken);
        _logger.LogTrace("... found {BuildCount} builds...", builds.Length);

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
                build => azDoClient.GetTimelineAsync(project.Project, build.Id, cancellationToken)
            );

        await Task.WhenAll(tasks.Select(s => s.Value));

        // Identify additional timelines by inspecting each record for a "PreviousAttempt"
        // object, then fetching the "timelineId" field.
        List<(Build build, Task<Timeline> timelineTask)> retriedTimelineTasks = new List<(Build, Task<Timeline>)>();
        foreach ((Build build, Task<Timeline> timelineTask) in tasks)
        {
            Timeline timeline = await timelineTask;

            if (timeline is null)
            {
                _logger.LogDebug("No timeline found for  {BuildId}", build.Id);
                continue;
            }

            IEnumerable<string> additionalTimelineIds = timeline.Records
                .Where(record => record.PreviousAttempts != null)
                .SelectMany(record => record.PreviousAttempts)
                .Select(attempt => attempt.TimelineId)
                .Distinct();

            retriedTimelineTasks.AddRange(
                additionalTimelineIds.Select(
                    timelineId => (build, azDoClient.GetTimelineAsync(project.Project, build.Id, timelineId, cancellationToken))));
        }

        await Task.WhenAll(retriedTimelineTasks.Select(o => o.timelineTask));

        // Only record timelines where their "lastChangedOn" field is after the last 
        // recorded date. Anything before has already been recorded.
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
            using IDisposable buildScope = _logger.BeginScope(KeyValuePair.Create("buildId", build.Id));

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
                var augRecord = new AugmentedTimelineRecord
                {
                    BuildId = build.Id,
                    TimelineId = timeline.Id,
                    Raw = record,
                };
                recordCache.Add(record.Id, augRecord);
                records.Add(augRecord);
                if (record.Issues == null)
                {
                    continue;
                }

                for (int iIssue = 0; iIssue < record.Issues.Length; iIssue++)
                {
                    var augIssue = new AugmentedTimelineIssue
                    {
                        BuildId = build.Id,
                        TimelineId = timeline.Id,
                        RecordId = record.Id,
                        Index = iIssue,
                        Raw = record.Issues[iIssue],
                    };
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

        await AddImageNamesToRecordsAsync(project, records, cancellationToken);

        _logger.LogInformation("Saving TimelineBuilds...");
        await _timelineTelemetryRepository.WriteTimelineBuilds(augmentedBuilds, project.Organization);

        _logger.LogInformation("Saving TimelineValidationMessages...");
        await _timelineTelemetryRepository.WriteTimelineValidationMessages(validationResults);

        _logger.LogInformation("Saving TimelineRecords...");
        await _timelineTelemetryRepository.WriteTimelineRecords(records);

        _logger.LogInformation("Saving TimelineIssues...");
        await _timelineTelemetryRepository.WriteTimelineIssues(issues);
    }

    private AugmentedBuild CreateAugmentedBuild(Build build)
    {
        string targetBranch = "";

        try
        {
            if (build.Reason == "pullRequest")
            {
                if (build.Parameters != null)
                {
                    targetBranch = (string)JObject.Parse(build.Parameters)["system.pullRequest.targetBranch"];
                }
                else
                {
                    _logger.LogInformation("Build parameters null, unable to extract target branch");
                }
            }
        }
        catch (JsonException e)
        {
            _logger.LogInformation(e, "Unable to extract targetBranch from Build");
        }

        return new AugmentedBuild
        {
            Build = build,
            TargetBranch = targetBranch,
        };
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

    private async Task AddImageNamesToRecordsAsync(AzureDevOpsProject project, List<AugmentedTimelineRecord> records, CancellationToken cancellationToken)
    {
        TimeSpan cancellationTime = TimeSpan.Parse(_options.LogScrapingTimeout ?? "00:10:00");

        try
        {
            _logger.LogInformation("Starting log scraping");

            var logScrapingTimeoutCancellationTokenSource = new CancellationTokenSource(cancellationTime);
            var logScrapingTimeoutCancellationToken = logScrapingTimeoutCancellationTokenSource.Token;

            using var combinedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, logScrapingTimeoutCancellationToken);
            var combinedCancellationToken = combinedCancellationTokenSource.Token;

            Stopwatch stopWatch = Stopwatch.StartNew();

            await GetImageNames(project, records, combinedCancellationToken);

            stopWatch.Stop();
            _logger.LogInformation("Log scraping took {ElapsedMilliseconds} milliseconds", stopWatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            //Don't swallow up the app cancellation token, let it do its thing
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown while getting image names");
        }
    }

    private async Task GetImageNames(AzureDevOpsProject project, List<AugmentedTimelineRecord> records, CancellationToken cancellationToken)
    {
        SemaphoreSlim throttleSemaphore = new SemaphoreSlim(50);

        var taskList = new List<Task>();

        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.Raw.Log?.Url))
            {
                continue;
            }

            if (record.Raw.Name == "Initialize job")
            {
                var childTask = GetImageName(project, record, throttleSemaphore, cancellationToken);
                taskList.Add(childTask);
                continue;
            }

            if (record.Raw.Name == "Initialize containers")
            {
                var childTask = GetDockerImageName(project, record, throttleSemaphore, cancellationToken);
                taskList.Add(childTask);
            }
        }

        try
        {
            await Task.WhenAll(taskList);
        }
        catch(Exception e)
        {                
            _logger.LogInformation("Log scraping had some failures `{Exception}`, summary below", e);
        }
        int successfulTasks = taskList.Count(task => task.IsCompletedSuccessfully);
        int cancelledTasks = taskList.Count(task => task.IsCanceled);
        int failedTasks = taskList.Count - successfulTasks - cancelledTasks;
        _logger.LogInformation("Log scraping summary: {SuccessfulTasks} successful, {CancelledTasks} cancelled, {FailedTasks} failed", successfulTasks, cancelledTasks, failedTasks);
    }
        
    private async Task GetImageName(AzureDevOpsProject project, AugmentedTimelineRecord record, SemaphoreSlim throttleSemaphore, CancellationToken cancellationToken)
    {
        await throttleSemaphore.WaitAsync(cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record.Raw.WorkerName.StartsWith("Azure Pipelines") || record.Raw.WorkerName.StartsWith("Hosted Agent"))
            {
                record.ImageName = await _buildLogScraper.ExtractMicrosoftHostedPoolImageNameAsync(project, record.Raw.Log.Url, cancellationToken);
            }
            else if (record.Raw.WorkerName.StartsWith("NetCore1ESPool-"))
            {
                record.ImageName = await _buildLogScraper.ExtractOneESHostedPoolImageNameAsync(project, record.Raw.Log.Url, cancellationToken);
            }
            else
            {
                record.ImageName = null;
            }
        }
        catch (Exception exception)
        {
            _logger.LogInformation(exception, "Non critical exception thrown when trying to get log '{LogUrl}'", record.Raw.Log.Url);
            throw;
        }
        finally
        {
            throttleSemaphore.Release();
        }
    }

    private async Task GetDockerImageName(AzureDevOpsProject project, AugmentedTimelineRecord record, SemaphoreSlim throttleSemaphore, CancellationToken cancellationToken)
    {
        await throttleSemaphore.WaitAsync(cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            record.ImageName = await _buildLogScraper.ExtractDockerImageNameAsync(project, record.Raw.Log.Url, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogInformation(exception, "Non critical exception thrown when trying to get log '{LogUrl}'", record.Raw.Log.Url);
            throw;
        }
        finally
        {
            throttleSemaphore.Release();
        }
    }
}
