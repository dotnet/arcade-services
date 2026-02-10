// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssues.WorkItems;
using Microsoft.Extensions.Logging;
using ProductConstructionService.WorkItems;

namespace BuildInsights.KnownIssues;

public interface IKnownIssuesAnalysisService
{
    Task RequestKnownIssuesAnalysis(string organization, string repository, long issueId);
}

public class KnownIssuesAnalysisProvider : IKnownIssuesAnalysisService
{
    private readonly ILogger<KnownIssuesAnalysisProvider> _logger;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;

    public KnownIssuesAnalysisProvider(
        IWorkItemProducerFactory workItemProducerFactory,
        ILogger<KnownIssuesAnalysisProvider> logger)
    {
        _logger = logger;
        _workItemProducerFactory = workItemProducerFactory;
    }

    public async Task RequestKnownIssuesAnalysis(string organization, string repository, long issueId)
    {
        var queueProducer = _workItemProducerFactory.CreateProducer<AnalysisProcessRequest>();

        _logger.LogInformation("Requesting known issues analysis for issue {organization}/{issueRepo}#{issueId}", organization, repository, issueId);

        await queueProducer.ProduceWorkItemAsync(
            new AnalysisProcessRequest
            {
                IssueId = issueId,
                Repository = organization + "/" + repository
            });
    }
}
