// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;
using BuildInsights.KnownIssues.WorkItems;
using Microsoft.Extensions.Logging;
using ProductConstructionService.WorkItems;

namespace BuildInsights.KnownIssuesProcessor;

public interface IRequestAnalysisService
{
    Task RequestAnalysisAsync(IReadOnlyList<Build> buildList);
}

public class RequestAnalysisProvider : IRequestAnalysisService
{
    private readonly ILogger<RequestAnalysisProvider> _logger;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;

    public RequestAnalysisProvider(
        IWorkItemProducerFactory workItemProducerFactory,
        ILogger<RequestAnalysisProvider> logger)
    {
        _logger = logger;
        _workItemProducerFactory = workItemProducerFactory;
    }

    public async Task RequestAnalysisAsync(IReadOnlyList<Build> buildList)
    {
        var producer = _workItemProducerFactory.CreateProducer<KnownIssueReprocessBuildWorkItem>(false);

        foreach (Build build in buildList)
        {
            _logger.LogInformation("Requesting reprocess of build {buildId}", build.Id);

            await producer.ProduceWorkItemAsync(new()
            {
                ProjectId = build.ProjectName,
                BuildId = build.Id,
                OrganizationId = build.OrganizationName
            });
        }
    }
}
