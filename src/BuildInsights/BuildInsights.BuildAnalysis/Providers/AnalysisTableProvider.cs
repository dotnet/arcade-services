// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Azure;
using Azure.Data.Tables;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Services;
using BuildInsights.Data;
using BuildInsights.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BuildInsights.BuildAnalysis.Providers;

public class BuildAnalysisHistoryProvider : IBuildAnalysisHistoryService
{
    private readonly BuildInsightsContext _context;

    public BuildAnalysisHistoryProvider(BuildInsightsContext context)
    {
        _context = context;
    }

    public async Task<BuildAnalysisEvent> GetLastBuildAnalysisRecord(int buildId, string definitionName)
    {
        return await _context.BuildAnalysisEvents
            .Where(e => e.PipelineName == definitionName && e.BuildId == buildId)
            .OrderByDescending(e => e.AnalysisTimestamp)
            .FirstOrDefaultAsync();
    }

    public async Task SaveBuildAnalysisRecords(ImmutableList<BuildResultAnalysis> completedPipelines, string repositoryId, string project, DateTimeOffset analysisTimestamp)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_tableSettings.Name, _tableSettings.Endpoint);
        foreach (BuildResultAnalysis analysis in completedPipelines)
        {
            BuildAnalysisEvent buildEvent = new BuildAnalysisEvent(analysis.PipelineName, analysis.BuildId, repositoryId, project, analysisTimestamp);
            await tableClient.UpsertEntityAsync(buildEvent);
        }
    }

    public async Task SaveBuildAnalysisRepositoryNotSupported(string pipeline, int buildId, string repositoryId, string project, DateTimeOffset analysisTimestamp)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_tableSettings.Name, _tableSettings.Endpoint);
        var buildEvent = new BuildAnalysisEvent(pipeline, buildId, repositoryId, project, analysisTimestamp, false);
        await tableClient.UpsertEntityAsync(buildEvent);
    }

    public async Task<List<BuildAnalysisEvent>> GetBuildsWithRepositoryNotSupported(DateTimeOffset since, CancellationToken cancellationToken)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_tableSettings.Name, _tableSettings.Endpoint);

        AsyncPageable<BuildAnalysisEvent> results = tableClient.QueryAsync<BuildAnalysisEvent>(
            buildAnalysisEvent => buildAnalysisEvent.IsRepositorySupported == false && buildAnalysisEvent.Timestamp > since, 100,
            cancellationToken: cancellationToken);

        var buildAnalysisNotSupported = new List<BuildAnalysisEvent>();
        await foreach (Page<BuildAnalysisEvent> page in results.AsPages().WithCancellation(cancellationToken))
        {
            buildAnalysisNotSupported.AddRange(page.Values);
        }

        return buildAnalysisNotSupported;
    }
}
