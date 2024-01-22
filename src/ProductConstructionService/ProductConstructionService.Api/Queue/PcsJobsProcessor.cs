// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.Text.Json;
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
    private const int _jobInvisibilityTimeoutSeconds = 30;
    private const int _emptyQueueTimeoutSeconds = 60;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        QueueClient queueClient = queueServiceClient.GetQueueClient(queueName);
        _logger.LogInformation("Starting to process PCS jobs {queueName}", queueName);
        while (!cancellationToken.IsCancellationRequested && status.ContinueWorking)
        {
            var pcsJob = await GetPcsJob(queueClient, cancellationToken);

            if (pcsJob == null)
            {
                // Queue is empty, wait a bit
                _logger.LogInformation("Queue {queueName} is empty. Sleeping for {sleepingTime} seconds", queueName, _emptyQueueTimeoutSeconds);
                await Task.Delay(TimeSpan.FromSeconds(_emptyQueueTimeoutSeconds), cancellationToken);
                continue;
            }

            ProcessPcsJob(pcsJob);
        }
        status.StoppedWorking = true;
        _logger.LogInformation("Stopped processing PCS jobs");
    }

    private async Task<PcsJob?> GetPcsJob(QueueClient queueClient, CancellationToken cancellationToken)
    {
        QueueMessage message = await queueClient.ReceiveMessageAsync(visibilityTimeout: TimeSpan.FromSeconds(_jobInvisibilityTimeoutSeconds), cancellationToken);
        if (message.Body == null)
        {
            return null;
        }

        await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);

        return JsonSerializer.Deserialize<PcsJob>(message.Body) ?? throw new Exception($"Failed to deserialize {message.Body} into a {nameof(PcsJob)}");
    }

    private void ProcessPcsJob(PcsJob pcsJob)
    {
        switch (pcsJob)
        {
            case TextPcsJob textPcsJob:
                _logger.LogInformation("Processed text job. Message: {message}", textPcsJob.Text);
                break;
            default:
                throw new NotSupportedException($"Unable to process unknown PCS job type: {pcsJob.GetType().Name}");
        }
    }
}
