// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Data;
using Azure.Data.Tables;
using BuildInsights.KnownIssues.Models;
using Kusto.Ingest;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildInsights.KnownIssues;

public interface IKnownIssuesService
{
    Task<ImmutableList<KnownIssueMatch>> GetKnownIssuesMatchesForIssue(int issueId, string issueRepository);
    Task<ImmutableList<TestKnownIssueMatch>> GetTestKnownIssuesMatchesForIssue(int issueId, string issueRepository);
    Task SaveKnownIssuesMatches(int buildId, List<KnownIssueMatch> knownIssueMatches);
    Task SaveTestsKnownIssuesMatches(int buildId, List<TestKnownIssueMatch> knownIssueMatches);
    Task SaveKnownIssuesHistory(IEnumerable<KnownIssue> knownIssues, int id);
}

public class KnownIssuesProvider : IKnownIssuesService
{
    private readonly IKustoClientProvider _kustoClientProvider;
    private readonly IKustoIngestClientFactory _kustoIngestClient;
    private readonly IOptions<KustoOptions> _kustoOptions;
    private readonly ISystemClock _clock;
    private readonly ILogger _logger;
    private const int TimeFilterDays = 30;
    private readonly IKnownIssuesHistoryService _knownIssuesHistoryService;

    public KnownIssuesProvider(
        IKustoClientProvider kustoClientProvider,
        IKustoIngestClientFactory kustoIngestClient,
        IOptions<KustoOptions> kustoOptions,
        ISystemClock clock,
        IKnownIssuesHistoryService knownIssuesHistoryService,
        ILogger<KnownIssuesProvider> logger)
    {
        _kustoClientProvider = kustoClientProvider;
        _kustoIngestClient = kustoIngestClient;
        _kustoOptions = kustoOptions;
        _clock = clock;
        _logger = logger;
        _knownIssuesHistoryService = knownIssuesHistoryService;
    }

    public async Task SaveKnownIssuesMatches(int buildId, List<KnownIssueMatch> knownIssueMatches)
    {
        _logger.LogInformation("Fetch already saved matched for build {0}", buildId);
        // Read matches already sent
        List<KnownIssueMatch> savedMatches = await GetSavedMatches(buildId, TimeFilterDays);
        List<KnownIssueMatch> newMatches = knownIssueMatches.Except(savedMatches).ToList();

        // Send new matches
        IKustoIngestClient client = _kustoIngestClient.GetClient();
        await KustoHelpers.WriteDataToKustoInMemoryAsync(
            client,
            _kustoOptions.Value.Database,
            "KnownIssues",
            _logger,
            newMatches,
            MapKnownIssueMatch);
    }

    public async Task SaveTestsKnownIssuesMatches(int buildId, List<TestKnownIssueMatch> knownIssueMatches)
    {
        _logger.LogInformation("Fetch already saved matched for build {0}", buildId);
        // Read matches already sent
        ImmutableList<TestKnownIssueMatch> savedMatches = await GetSavedTestsKnownIssuesMatches(buildId, TimeFilterDays);
        List<TestKnownIssueMatch> newMatches = knownIssueMatches.Except(savedMatches).ToList();

        // Send new matches
        IKustoIngestClient client = _kustoIngestClient.GetClient();
        await KustoHelpers.WriteDataToKustoInMemoryAsync(
            client,
            _kustoOptions.Value.Database,
            "TestKnownIssues",
            _logger,
            newMatches,
            MapKnownIssueMatch);
    }

    public static KustoValue[] MapKnownIssueMatch(KnownIssueMatch match)
    {
        return
        [
            new KustoValue("BuildId", match.BuildId, KustoDataType.Int),
            new KustoValue("BuildRepository", match.BuildRepository, KustoDataType.String),
            new KustoValue("StepStartTime", match.StepStartTime?.ToIso8601String(), KustoDataType.DateTime),
            new KustoValue("IssueId", match.IssueId, KustoDataType.Int),
            new KustoValue("IssueRepository", match.IssueRepository, KustoDataType.String),
            new KustoValue("IssueType", match.IssueType, KustoDataType.String),
            new KustoValue("IssueLabels", match.IssueLabels, KustoDataType.String),
            new KustoValue("JobId", match.JobId, KustoDataType.String),
            new KustoValue("StepName", match.StepName, KustoDataType.String),
            new KustoValue("LogURL", match.LogURL, KustoDataType.String),
            new KustoValue("PullRequest", match.PullRequest, KustoDataType.String),
            new KustoValue("Project", match.Project, KustoDataType.String),
            new KustoValue("Organization", match.Organization, KustoDataType.String)
        ];
    }

    private static KustoValue[] MapKnownIssueMatch(TestKnownIssueMatch match)
    {
        return
        [
            new KustoValue("BuildId", match.BuildId, KustoDataType.Int),
            new KustoValue("BuildRepository", match.BuildRepository, KustoDataType.String),
            new KustoValue("CompletedDate", match.CompletedDate?.ToIso8601String(), KustoDataType.DateTime),
            new KustoValue("IssueId", match.IssueId, KustoDataType.Int),
            new KustoValue("IssueRepository", match.IssueRepository, KustoDataType.String),
            new KustoValue("IssueType", match.IssueType, KustoDataType.String),
            new KustoValue("IssueLabels", match.IssueLabels, KustoDataType.String),
            new KustoValue("TestResultName", match.TestResultName, KustoDataType.String),
            new KustoValue("TestRunId", match.TestRunId, KustoDataType.Int),
            new KustoValue("Url", match.Url, KustoDataType.String),
            new KustoValue("PullRequest", match.PullRequest, KustoDataType.String),
            new KustoValue("Project", match.Project, KustoDataType.String),
            new KustoValue("Organization", match.Organization, KustoDataType.String)
        ];
    }

    public async Task<List<KnownIssueMatch>> GetSavedMatches(int buildId, int lastNDays)
    {
        var query = new KustoQuery(@"
KnownIssues
| where BuildId == _buildId and StepStartTime > _datefilter
| project BuildId, BuildRepository, StepStartTime, IssueId, IssueRepository, IssueType, JobId, StepName, LogURL, PullRequest, Project, Organization");
        query.AddParameter("_buildId", buildId, KustoDataType.Int);
        query.AddParameter("_datefilter", _clock.UtcNow.AddDays(-lastNDays), KustoDataType.DateTime);

        using IDataReader reader = await _kustoClientProvider.ExecuteKustoQueryAsync(query).ConfigureAwait(false);

        return GetKnownIssueFromDataReader(reader);
    }

    public async Task<ImmutableList<KnownIssueMatch>> GetKnownIssuesMatchesForIssue(int issueId, string issueRepository)
    {
        var query = new KustoQuery(@"
KnownIssues
| extend StepStartTime = iff(isempty(StepStartTime), ingestion_time(), StepStartTime)
| where StepStartTime > _dateFilter
| where IssueId == _issueId
| where IssueRepository == _issueRepository
| summarize arg_max(StepStartTime, *) by BuildId, IssueRepository, IssueId
| project BuildId, BuildRepository, StepStartTime, IssueId, IssueRepository, IssueType, JobId, StepName, LogURL, PullRequest, Project, Organization");
        query.AddParameter("_issueId", issueId, KustoDataType.Int);
        query.AddParameter("_issueRepository", issueRepository, KustoDataType.String);
        query.AddParameter("_dateFilter", _clock.UtcNow.AddDays(-TimeFilterDays), KustoDataType.DateTime);

        IDataReader reader = await _kustoClientProvider.ExecuteKustoQueryAsync(query);

        return GetKnownIssueFromDataReader(reader).ToImmutableList();
    }

    private async Task<ImmutableList<TestKnownIssueMatch>> GetSavedTestsKnownIssuesMatches(int buildId, int lastNDays)
    {
        var query = new KustoQuery(@"
TestKnownIssues
| where BuildId == _buildId and CompletedDate > _datefilter
| project BuildId, BuildRepository, CompletedDate, IssueId, IssueRepository, IssueType, TestResultName, TestRunId, Url, PullRequest, Project, Organization");
        query.AddParameter("_buildId", buildId, KustoDataType.Int);
        query.AddParameter("_datefilter", _clock.UtcNow.AddDays(-lastNDays), KustoDataType.DateTime);

        using IDataReader reader = await _kustoClientProvider.ExecuteKustoQueryAsync(query).ConfigureAwait(false);

        return GetTestsKnownIssuesFromDataReader(reader);
    }

    public async Task<ImmutableList<TestKnownIssueMatch>> GetTestKnownIssuesMatchesForIssue(int issueId, string repository)
    {
        var query = new KustoQuery(@"
TestKnownIssues
| extend CompletedDate = iff(isempty(CompletedDate), ingestion_time(), CompletedDate)
| where IssueId == _issueId and IssueRepository == _issueRepository and CompletedDate > _dateFilter
| summarize arg_max(CompletedDate, *) by BuildId, IssueRepository, IssueId
| project BuildId, BuildRepository, CompletedDate, IssueId, IssueRepository, IssueType, TestResultName, TestRunId, Url, PullRequest, Project, Organization");
        query.AddParameter("_issueId", issueId, KustoDataType.Int);
        query.AddParameter("_issueRepository", repository, KustoDataType.String);
        query.AddParameter("_dateFilter", _clock.UtcNow.AddDays(-TimeFilterDays), KustoDataType.DateTime);

        using IDataReader reader = await _kustoClientProvider.ExecuteKustoQueryAsync(query).ConfigureAwait(false);

        return GetTestsKnownIssuesFromDataReader(reader);
    }

    public List<KnownIssueMatch> GetKnownIssueFromDataReader(IDataReader reader)
    {
        var knownIssueMatches = new List<KnownIssueMatch>();
        while (reader.Read())
        {
            var knownIssueMatch = new KnownIssueMatch
            {
                BuildId = (int)reader.GetInt32(0),
                BuildRepository = SqlHelper.GetReaderValue(reader, 1, reader.GetString),
                StepStartTime = SqlHelper.GetNullableReaderValue(reader, 2, reader.GetDateTimeOffset),
                IssueId = (int)reader.GetInt32(3),
                IssueRepository = SqlHelper.GetReaderValue(reader, 4, reader.GetString),
                IssueType = SqlHelper.GetReaderValue(reader, 5, reader.GetString),
                JobId = SqlHelper.GetReaderValue(reader, 6, reader.GetString),
                StepName = SqlHelper.GetReaderValue(reader, 7, reader.GetString),
                LogURL = SqlHelper.GetReaderValue(reader, 8, reader.GetString),
                PullRequest = SqlHelper.GetReaderValue(reader, 9, reader.GetString),
                Project = SqlHelper.GetReaderValue(reader, 10, reader.GetString),
                Organization = SqlHelper.GetReaderValue(reader, 11, reader.GetString)
            };
            knownIssueMatches.Add(knownIssueMatch);
        }
        return knownIssueMatches;
    }

    private ImmutableList<TestKnownIssueMatch> GetTestsKnownIssuesFromDataReader(IDataReader reader)
    {
        var knownIssueMatches = new List<TestKnownIssueMatch>();
        while (reader.Read())
        {
            var knownIssueMatch = new TestKnownIssueMatch
            {
                BuildId = reader.GetInt32(0),
                BuildRepository = SqlHelper.GetReaderValue(reader, 1, reader.GetString),
                CompletedDate = SqlHelper.GetNullableReaderValue(reader, 2, reader.GetDateTimeOffset),
                IssueId = reader.GetInt32(3),
                IssueRepository = SqlHelper.GetReaderValue(reader, 4, reader.GetString),
                IssueType = SqlHelper.GetReaderValue(reader, 5, reader.GetString),
                TestResultName = SqlHelper.GetReaderValue(reader, 6, reader.GetString),
                TestRunId = reader.GetInt32(7),
                Url = SqlHelper.GetReaderValue(reader, 8, reader.GetString),
                PullRequest = SqlHelper.GetReaderValue(reader, 9, reader.GetString),
                Project = SqlHelper.GetReaderValue(reader, 10, reader.GetString),
                Organization = SqlHelper.GetReaderValue(reader, 11, reader.GetString)
            };
            knownIssueMatches.Add(knownIssueMatch);
        }
        return knownIssueMatches.ToImmutableList();
    }

    public async Task SaveKnownIssuesHistory(IEnumerable<KnownIssue> knownIssues, int buildId)
    {
        await _knownIssuesHistoryService.SaveKnownIssuesHistory(knownIssues, buildId);
    }
}
