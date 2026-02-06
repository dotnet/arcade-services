using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;
using Microsoft.Internal.Helix.Utility.Azure;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

public class BuildProcessingStatusStatusProvider : IBuildProcessingStatusService
{
    private readonly ProcessingStatusTableConnectionSettings _statusTableSettings;
    private readonly ITableClientFactory _tableClientFactory;

    public BuildProcessingStatusStatusProvider(
        IOptions<ProcessingStatusTableConnectionSettings> statusTableSettings,
        ITableClientFactory tableClientFactory)
    {
        _statusTableSettings = statusTableSettings.Value;
        _tableClientFactory = tableClientFactory;
    }

    public async Task<bool> IsBuildBeingProcessed(DateTimeOffset since, string repository, int buildId, CancellationToken cancellationToken)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_statusTableSettings.Name, _statusTableSettings.Endpoint);

        AsyncPageable<BuildProcessingStatusEvent> results = tableClient.QueryAsync<BuildProcessingStatusEvent>(
            buildProcessingEvent => buildProcessingEvent.PartitionKey == NormalizeRepository(repository) &&
                                    buildProcessingEvent.RowKey == buildId.ToString() &&
                                    buildProcessingEvent.Status == BuildProcessingStatus.InProcess.Value &&
                                    buildProcessingEvent.Timestamp > since, 100, cancellationToken: cancellationToken);

        var buildAnalysisInProcess = new List<BuildProcessingStatusEvent>();
        await foreach (Page<BuildProcessingStatusEvent> page in results.AsPages().WithCancellation(cancellationToken))
        {
            buildAnalysisInProcess.AddRange(page.Values);
        }

        return buildAnalysisInProcess.Any();
    }

    public async Task SaveBuildAnalysisProcessingStatus(string repository, int buildId, BuildProcessingStatus processingStatus)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_statusTableSettings.Name, _statusTableSettings.Endpoint);

        var buildEvent = new BuildProcessingStatusEvent(NormalizeRepository(repository), buildId, processingStatus);
        await tableClient.UpsertEntityAsync(buildEvent);
    }

    public async Task SaveBuildAnalysisProcessingStatus(List<(string repository, int buildId)> builds, BuildProcessingStatus processingStatus)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_statusTableSettings.Name, _statusTableSettings.Endpoint);

        IEnumerable<BuildProcessingStatusEvent> buildProcessingStatusEvents = builds.Select(build =>
            new BuildProcessingStatusEvent(NormalizeRepository(build.repository), build.buildId, processingStatus));

        foreach (BuildProcessingStatusEvent buildProcessingStatusEvent in buildProcessingStatusEvents)
        {
            await tableClient.UpsertEntityAsync(buildProcessingStatusEvent);
        }
    }

    public async Task<List<BuildProcessingStatusEvent>> GetBuildsWithOverrideConclusion(DateTimeOffset since, CancellationToken cancellationToken)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_statusTableSettings.Name, _statusTableSettings.Endpoint);

        AsyncPageable<BuildProcessingStatusEvent> results = tableClient.QueryAsync<BuildProcessingStatusEvent>(
            buildProcessingEvent => buildProcessingEvent.Timestamp > since &&
                                    buildProcessingEvent.Status == BuildProcessingStatus.ConclusionOverridenByUser.Value,
            100, cancellationToken: cancellationToken);

        var buildAnalysisInProcess = new List<BuildProcessingStatusEvent>();
        await foreach (Page<BuildProcessingStatusEvent> page in results.AsPages().WithCancellation(cancellationToken))
        {
            buildAnalysisInProcess.AddRange(page.Values);
        }

        return buildAnalysisInProcess.ToList();
    }


    private static string NormalizeRepository(string repository)
    {
        return $"{repository.Replace('/', '.')}";
    }
}
