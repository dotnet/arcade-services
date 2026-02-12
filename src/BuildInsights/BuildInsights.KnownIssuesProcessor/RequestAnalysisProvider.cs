using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.KnownIssues.Models;
using Microsoft.Internal.Helix.KnownIssuesProcessor.Services;
using Microsoft.Internal.Helix.Utility.Azure;

namespace BuildInsights.KnownIssuesProcessor;

public interface IRequestAnalysisService
{
    Task RequestAnalysisAsync(IReadOnlyList<Build> buildList);
}

public class RequestAnalysisProvider : IRequestAnalysisService
{
    private readonly IOptionsMonitor<KnownIssuesProcessorOptions> _options;
    private readonly ILogger<RequestAnalysisProvider> _logger;
    private readonly IQueueClientFactory _queueClientFactory;

    private QueueClientOptions queueClientOptions = new QueueClientOptions()
    {
        MessageEncoding = QueueMessageEncoding.Base64
    };

    private QueueClient queueClient => _queueClientFactory.GetQueueClient(_options.CurrentValue.BuildAnalysisQueueName, _options.CurrentValue.BuildAnalysisQueueEndpoint, queueClientOptions);

    public RequestAnalysisProvider(
        IQueueClientFactory queueClientFactory,
        IOptionsMonitor<KnownIssuesProcessorOptions> options,
        ILogger<RequestAnalysisProvider> logger)
    {
        _options = options;
        _logger = logger;
        _queueClientFactory = queueClientFactory;
    }

    public async Task RequestAnalysisAsync(IReadOnlyList<Build> buildList)
    {
        foreach (Build build in buildList)
        {
            _logger.LogInformation("Requesting reprocess of build {buildId}", build.Id);

            KnownIssueReprocessBuildMessage knownIssueReprocessBuildMessage = new KnownIssueReprocessBuildMessage
            {
                ProjectId = build.ProjectName,
                BuildId = build.Id,
                OrganizationId = build.OrganizationName
            };

            string jsonMessage = JsonSerializer.Serialize(knownIssueReprocessBuildMessage);

            await queueClient.SendMessageAsync(jsonMessage);
        }
    }
}
