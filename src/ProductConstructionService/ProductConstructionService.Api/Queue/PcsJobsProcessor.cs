// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using ProductConstructionService.Api.Queue.WorkItems;

namespace ProductConstructionService.Api.Queue;

public class PcsJobsProcessor(
    ILogger<PcsJobsProcessor> logger,
    string queueName,
    PcsJobsProcessorStatus status,
    QueueServiceClient queueServiceClient)
    : BackgroundService
{
    private readonly ILogger<PcsJobsProcessor> _logger = logger;
    private const int jobInvisibilityTimeout = 30;

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        QueueClient queueClient = queueServiceClient.GetQueueClient(queueName);
        _logger.LogInformation("Starting to listen to {queueName}", queueName);
        while (!stoppingToken.IsCancellationRequested && status.ContinueWorking)
        {
            var message = queueClient.ReceiveMessage();
            _logger.LogInformation($"{message.Value.Body.ToString()}");
            await Task.Delay(5000);
        }
        status.StoppedWorking = true;
        _logger.LogInformation("Done working!");
    }

    /*private async Task<PcsJob?> GetPcsJob(QueueClient queueClient)
    {
        NullableResponse<QueueMessage> response = await queueClient.ReceiveMessageAsync(visibilityTimeout: TimeSpan.FromSeconds(jobInvisibilityTimeout));
        if (response.HasValue == null)
        {
            return null;
        }

    }*/
}
