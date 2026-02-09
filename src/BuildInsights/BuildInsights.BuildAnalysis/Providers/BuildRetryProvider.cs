// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Services;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace BuildInsights.BuildAnalysis.Providers;

public class BuildRetryProvider : IBuildRetryService
{
    private const string ArtifactName = "BuildConfiguration";
    private const int InternalRetryLimit = 10;
    private readonly string _fileName;
    private readonly IBuildDataService _buildDataService;
    private readonly IBuildOperationsService _buildOperations;
    private readonly ILogger<BuildRetryProvider> _logger;

    public BuildRetryProvider(
        IBuildDataService buildDataService,
        IBuildOperationsService buildOperations,
        IOptions<BuildConfigurationFileSettings> buildConfigurationFileSettings,
        ILogger<BuildRetryProvider> logger
    )
    {
        _buildDataService = buildDataService;
        _buildOperations = buildOperations;
        _logger = logger;
        _fileName = buildConfigurationFileSettings.Value.FileName;
    }

    public async Task<bool> RetryIfSuitable(string orgId, string projectId, int buildId,
        CancellationToken cancellationToken = default)
    {
        if (await IsBuildSuitableToRetry(orgId, projectId, buildId, cancellationToken))
        {
            _logger.LogInformation("buildId: {buildId} in org {orgId} meets the requirements to be retried", buildId, orgId);
            return await _buildOperations.RetryBuild(orgId, projectId, buildId, cancellationToken);
        }

        _logger.LogInformation("buildId: {buildId} in {ordId}, doesn't meet the requirements to be retried", buildId, orgId);
        return false;
    }

    public async Task<bool> RetryIfKnownIssueSuitable(string orgId, string projectId, int buildId, 
        CancellationToken cancellationToken = default)
    {

        //The build should be in failing status and completed, to be sure that hasn't be restarted yet and that needs to be retry
        Build build = await _buildDataService.GetBuildAsync(orgId, projectId, buildId, cancellationToken);
        if (build.Result != BuildResult.Failed)
        {
            return false;
        }

        IReadOnlyList<TimelineRecord> records = await _buildDataService.GetLatestBuildTimelineRecordsAsync(orgId, projectId, buildId, cancellationToken);
        if (records.Count < 1) return false;

        int attempt = records.Max(r => r.Attempt);
        if (attempt > KnownIssuesConstants.AttemptLimit || attempt > InternalRetryLimit)
        {
            return false;
        }

        return await _buildOperations.RetryBuild(orgId, projectId, buildId, cancellationToken);
    }

    private async Task<bool> IsBuildSuitableToRetry(string orgId, string projectId, int buildId, CancellationToken cancellationToken = default)
    {
        //The build should be in failing status and completed, to be sure that hasn't be restarted yet and that needs to be retry
        Build build = await _buildDataService.GetBuildAsync(orgId, projectId, buildId, cancellationToken);
        if (build.Result != BuildResult.Failed)
        {
            return false;
        }

        BuildConfiguration buildConfiguration = await _buildDataService.GetBuildConfiguration(orgId, projectId, buildId, ArtifactName, _fileName, cancellationToken);
         if (buildConfiguration == null) return false;

        IReadOnlyList<TimelineRecord> records = await _buildDataService.GetLatestBuildTimelineRecordsAsync(orgId, projectId, buildId, cancellationToken);
        if (records.Count < 1) return false;

        int attempt = records.OrderByDescending(r => r.Attempt).Select(r => r.Attempt).First();
        if (attempt > buildConfiguration.RetryCountLimit || attempt > InternalRetryLimit)
        {
            _logger.LogInformation("buildId: {buildId} in {orgId}, is NOT suitable for retry because has been retry {attempt} and the retry limit is {RetryCountLimit}",
                buildId, orgId, attempt, buildConfiguration.RetryCountLimit);
            return false;
        }

        if (buildConfiguration.RetryByAnyError)
        {
            _logger.LogInformation("buildId: {buildId} in {orgId} is suitable for retry after matching the 'RetryByAnyError' rule", buildId, orgId);
            return true;
        }

        if (IsRetryByErrors(buildConfiguration.RetryByErrors, records, out  string regexMatch))
        {
            _logger.LogInformation("buildId: {buildId} in {orgId} is suitable for retry after matching the 'RetryByErrors' rule with regex match: {regexMatch}",
                buildId, orgId, regexMatch);
            return true;
        }

        if (IsRetryByPipeline(buildConfiguration.RetryByPipeline, records, buildId))
        {
            return true;
        }

        return IsRetryByErrorsInPipeline(buildConfiguration.RetryByErrorsInPipeline, records, buildId);
    }

    private  bool IsRetryByErrors(IReadOnlyCollection<Errors> errors, IReadOnlyList<TimelineRecord> timelineRecords, out string regexMatch)
    {
        regexMatch = null;
        if (errors == null) return false;

        List<string> pipelineErrors = timelineRecords.Where(t => t.Result == TaskResult.Failed).SelectMany(t => t.Issues)
            .Select(i => i.Message).ToList();
        List<string> errorsRegex = errors.Select(e => e.ErrorRegex).ToList();

        regexMatch = errorsRegex.FirstOrDefault(p => pipelineErrors.Any(e => Regex.IsMatch(e, p)));
        return regexMatch != null;
    }

    private bool IsRetryByError(string errorRegex, IReadOnlyList<TimelineRecord> timelineRecords, out string regexMatch)
    {
        regexMatch = null;
        if (errorRegex == null) return false;

        List<string> pipelineErrors = timelineRecords.Where(t => t.Result == TaskResult.Failed).SelectMany(t => t.Issues)
            .Select(i => i.Message).ToList();

        regexMatch =  pipelineErrors.FirstOrDefault(e => Regex.IsMatch(e, errorRegex));
        return regexMatch != null;
    }

    private bool IsRetryByPipeline(Pipeline pipelineToRetry, IReadOnlyList<TimelineRecord> timelineRecords, int buildId)
    {
        if (pipelineToRetry == null) return false;

        if (IsMatchingRecordFailing(pipelineToRetry.RetryJobs?.Select(j => j.JobName).ToList(), RecordType.Job, timelineRecords, out string matchJob))
        {
            _logger.LogInformation("{buildId} is suitable to retry after a matching the 'RetryByJob' rule with Job: {matchJob}", buildId, matchJob);
            return true;
        }

        if (IsMatchingRecordFailing(pipelineToRetry.RetryPhases?.Select(p => p.PhaseName).ToList(), RecordType.Phase, timelineRecords, out string matchPhase))
        {
            _logger.LogInformation("{buildId} is suitable to retry after a matching the 'RetryByPhase' rule with Phase: {matchPhase}", buildId, matchPhase);
            return true;
        }

        if (IsMatchingRecordFailing(pipelineToRetry.RetryStages?.Select(p => p.StageName).ToList(), RecordType.Stage, timelineRecords, out string matchStage))
        {
            _logger.LogInformation("{buildId} is suitable to retry after a matching the 'RetryByStage' rule with Stage: {matchStage}", buildId, matchStage);
            return true;
        }

        foreach (JobsInStage jobInStage in pipelineToRetry.RetryJobsInStage)
        {
            List<TimelineRecord> matchingStage = GetFilteredTimelineRecords(ImmutableList.Create(jobInStage.StageName), RecordType.Stage, timelineRecords);
            if (IsMatchingRecordFailing(jobInStage.JobsNames, RecordType.Job, GetChildRecords(matchingStage, timelineRecords), out string matchStageJob))
            {
                _logger.LogInformation("{buildId} is suitable to retry after a matching the 'RetryJobsInStage' rule with Stage: {StageName} and Job: {matchStageJob}",
                    buildId, jobInStage.StageName, matchStageJob);
                return true;
            }
        }

        return false;
    }

    private bool IsRetryByErrorsInPipeline(RetryByErrorsInPipeline retryByErrorsInPipeline, IReadOnlyList<TimelineRecord> timelineRecords, int buildId)
    {
        if (retryByErrorsInPipeline == null) return false;

        string retryErrorMatch;

        foreach (ErrorInPipelineByJobs errorInPipelineByJob in retryByErrorsInPipeline.ErrorInPipelineByJobs)
        {
            List<TimelineRecord> matchingJobs = GetFilteredTimelineRecords(errorInPipelineByJob.JobsNames, RecordType.Job, timelineRecords);
            if (IsRetryByError(errorInPipelineByJob.ErrorRegex, GetChildRecords(matchingJobs, timelineRecords), out retryErrorMatch))
            {
                _logger.LogInformation("buildId: {buildId} is suitable for retry after matching the 'IsRetryByErrorsInPipeline' in Job with regex match: {retryErrorMatch}",
                    buildId, retryErrorMatch);
                return true;
            }
        }

        foreach (ErrorInPipelineByStage errorInPipelineByStage in retryByErrorsInPipeline.ErrorInPipelineByStage)
        {
            List<TimelineRecord> matchingStage = GetFilteredTimelineRecords(ImmutableList.Create(errorInPipelineByStage.StageName), RecordType.Stage, timelineRecords);
            if (IsRetryByError(errorInPipelineByStage.ErrorRegex, GetChildRecords(matchingStage, timelineRecords), out retryErrorMatch))
            {
                _logger.LogInformation(
                    "buildId: {buildId} is suitable for retry after matching the 'IsRetryByErrorsInPipeline' rule in Stage: {StageName} with regex match: {retryErrorMatch}",
                    buildId, errorInPipelineByStage.StageName, retryErrorMatch);
                return true;
            }
        }

        foreach (ErrorInPipelineByJobsInStage errorInPipelineByJobsInStage in retryByErrorsInPipeline.ErrorInPipelineByJobsInStage)
        {
            List<TimelineRecord> matchingStage = GetFilteredTimelineRecords(ImmutableList.Create(errorInPipelineByJobsInStage.StageName), RecordType.Stage, timelineRecords);
            IReadOnlyList<TimelineRecord> recordsInStage = GetChildRecords(matchingStage, timelineRecords);
            List<TimelineRecord> jobsInStage = GetFilteredTimelineRecords(errorInPipelineByJobsInStage.JobsNames, RecordType.Job, recordsInStage);
            if (IsRetryByError(errorInPipelineByJobsInStage.ErrorRegex, GetChildRecords(jobsInStage, recordsInStage), out retryErrorMatch))
            {
                _logger.LogInformation(
                    "buildId: {buildId} is suitable for retry after matching the 'IsRetryByErrorsInPipeline' rule in Stage: {StageName} with regex match: {retryErrorMatch}",
                    buildId, errorInPipelineByJobsInStage.StageName, retryErrorMatch);
                return true;
            }
        }

        return false;
    }

    private bool IsMatchingRecordFailing(IReadOnlyCollection<string> pipelineRetryNames, RecordType type, IReadOnlyList<TimelineRecord> timelineRecords, out string pipelineMatch)
    {
        if (pipelineRetryNames == null)
        {
            pipelineMatch = null;
            return false;
        }

        pipelineMatch = pipelineRetryNames.FirstOrDefault(s => timelineRecords.Any(t =>
            t.Result == TaskResult.Failed && t.RecordType == type &&
            (s.Equals(t.Name, StringComparison.OrdinalIgnoreCase) ||
             s.Equals(t.Identifier, StringComparison.OrdinalIgnoreCase))));

        return pipelineMatch != null;
    }

    private List<TimelineRecord> GetFilteredTimelineRecords(IReadOnlyCollection<string> matchingRecords, RecordType type, IReadOnlyList<TimelineRecord> timelineRecords)
    {
        if (matchingRecords == null) return new List<TimelineRecord>();

        return matchingRecords.SelectMany(m => timelineRecords.Where(t =>
                t.RecordType == type && m != null && (m.Equals(t.Name, StringComparison.OrdinalIgnoreCase) ||
                                                      m.Equals(t.Identifier, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    private IReadOnlyList<TimelineRecord> GetChildRecords(List<TimelineRecord> parentRecords, IReadOnlyList<TimelineRecord> timelineRecords)
    {
        var childRecords = new List<TimelineRecord>();
        foreach (TimelineRecord parentRecord in parentRecords ?? new List<TimelineRecord>())
        {
            DepthFirstTraversal(timelineRecords, parentRecord.Id, childRecords.Add);
        }

        return childRecords.Where(t => t != null).ToList();
    }

    private static void DepthFirstTraversal(IReadOnlyList<TimelineRecord> timelineRecords, Guid parent, Action<TimelineRecord> visit)
    {
        visit(timelineRecords.FirstOrDefault(t => t.Id == parent));

        foreach (Guid childGuid in timelineRecords.Where(t => t.ParentId == parent).Select(t => t.Id))
        {
            DepthFirstTraversal(timelineRecords, childGuid, visit);
        }
    }
}
