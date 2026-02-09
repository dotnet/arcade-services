// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using BuildInsights.KnownIssues.Models;
using BuildInsights.KnownIssues.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Internal.Helix.Utility.Azure;

namespace BuildInsights.KnownIssues.Providers;

public class KnownIssuesAnalysisProvider : IKnownIssuesAnalysisService
{
    private readonly ILogger<KnownIssuesAnalysisProvider> _logger;
    private readonly IQueueClientFactory _queueClientFactory;
    private readonly KnownIssuesAnalysisOptions _options;

    private QueueClientOptions _queueClientOptions = new QueueClientOptions()
    {
        MessageEncoding = QueueMessageEncoding.Base64
    };

    public KnownIssuesAnalysisProvider(
        IQueueClientFactory queueClientFactory,
        IOptions<KnownIssuesAnalysisOptions> options,
        ILogger<KnownIssuesAnalysisProvider> logger)
    {
        _logger = logger;
        _queueClientFactory = queueClientFactory;
        _options = options.Value;
    }

    public async Task RequestKnownIssuesAnalysis(string organization, string repository, long issueId)
    {
        QueueClient queueClient = _queueClientFactory.GetQueueClient(_options.Name, _options.Endpoint, _queueClientOptions);

        _logger.LogInformation("Requesting known issues analysis for issue {organization}/{issueRepo}#{issueId}", organization, repository, issueId);
        AnalysisProcessRequest buildAnalysisMessage = new AnalysisProcessRequest
        {
            IssueId = issueId,
            Repository = organization + "/" + repository
        };

        string jsonMessaje = JsonSerializer.Serialize(buildAnalysisMessage);

        await queueClient.SendMessageAsync(jsonMessaje);
    }
}
