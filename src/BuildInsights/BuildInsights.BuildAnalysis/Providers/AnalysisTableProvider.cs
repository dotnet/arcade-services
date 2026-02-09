// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Services;
using Microsoft.Internal.Helix.Utility.Azure;

namespace BuildInsights.BuildAnalysis.Providers;

public class BuildAnalysisHistoryProvider : IBuildAnalysisHistoryService
{
    private readonly BuildAnalysisTableConnectionSettings _tableSettings;
    private readonly ITableClientFactory _tableClientFactory;

    public BuildAnalysisHistoryProvider(
        ITableClientFactory tableClientFactory,
        IOptions<BuildAnalysisTableConnectionSettings> tableSettings)
    {
        _tableSettings = tableSettings.Value;
        _tableClientFactory = tableClientFactory;
    }

    public BuildAnalysisEvent GetLastBuildAnalysisRecord(int buildId, string definitionName)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_tableSettings.Name, _tableSettings.Endpoint);
        Pageable<BuildAnalysisEvent> results = tableClient.Query<BuildAnalysisEvent>(e => e.PartitionKey == definitionName && e.RowKey == buildId.ToString());
        return results.FirstOrDefault();
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
