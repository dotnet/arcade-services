// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.Text.Json;
using System.Threading;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using ProductConstructionService.Api.Queue.WorkItems;

namespace ProductConstructionService.Api.Queue;

public class PcsJobsProcessor(
    ILogger<PcsJobsProcessor> logger,
    PcsJobProcessorOptions options,
    PcsJobsProcessorStatus status,
    QueueServiceClient queueServiceClient)
    : BackgroundService
{
    private readonly ILogger<PcsJobsProcessor> _logger = logger;
    private readonly PcsJobProcessorOptions _options = options;
    private readonly PcsJobsProcessorStatus _status = status;

    private const int JobInvisibilityTimeoutSeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.Run(async () => {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _status.Semaphore.WaitAsync(cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await ProcessJobs(cancellationToken);
                }
            }
        }, cancellationToken);
    }

    private async Task ProcessJobs(CancellationToken cancellationToken)
    {
        QueueClient queueClient = queueServiceClient.GetQueueClient(_options.QueueName);
        _logger.LogInformation("Starting to process PCS jobs {queueName}", _options.QueueName);
        try
        {
            while (!cancellationToken.IsCancellationRequested && _status.State == PcsJobsProcessorState.Working)
            {
                try
                {
                    var pcsJob = await GetPcsJob(queueClient, cancellationToken);

                    if (pcsJob == null)
                    {
                        // Queue is empty, wait a bit
                        _logger.LogInformation("Queue {queueName} is empty. Sleeping for {sleepingTime} seconds", _options.QueueName, _options.EmptyQueueWaitTime);
                        await Task.Delay(_options.EmptyQueueWaitTime, cancellationToken);
                        continue;
                    }

                    ProcessPcsJob(pcsJob);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception while processing pcs job");
                }
            }

        }
        finally
        {
            _status.State = PcsJobsProcessorState.StoppedWorking;
            _logger.LogInformation("Stopped processing PCS jobs");
        }
    }

    private async Task<PcsJob?> GetPcsJob(QueueClient queueClient, CancellationToken cancellationToken)
    {
        QueueMessage message = await queueClient.ReceiveMessageAsync(visibilityTimeout: TimeSpan.FromSeconds(JobInvisibilityTimeoutSeconds), cancellationToken);
        if (message == null || message.Body == null)
        {
            return null;
        }

        await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);

        return message.Body.ToObjectFromJson<PcsJob>() ?? throw new Exception($"Failed to deserialize {message.Body} into a {nameof(PcsJob)}");
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
