// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.Text.Json;
using System.Threading;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue;

public class JobsProcessor(
    ILogger<JobsProcessor> logger,
    JobProcessorOptions options,
    JobsProcessorStatus status,
    QueueServiceClient queueServiceClient)
    : BackgroundService
{
    private readonly ILogger<JobsProcessor> _logger = logger;
    private readonly JobProcessorOptions _options = options;
    private readonly JobsProcessorStatus _status = status;

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
            while (!cancellationToken.IsCancellationRequested && _status.State == JobsProcessorState.Working)
            {
                try
                {
                    var job = await GetJob(queueClient, cancellationToken);

                    if (job == null)
                    {
                        // Queue is empty, wait a bit
                        _logger.LogInformation("Queue {queueName} is empty. Sleeping for {sleepingTime} seconds", _options.QueueName, _options.EmptyQueueWaitTime);
                        await Task.Delay(_options.EmptyQueueWaitTime, cancellationToken);
                        continue;
                    }

                    ProcessJob(job);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception while processing pcs job");
                }
            }

        }
        finally
        {
            _status.State = JobsProcessorState.StoppedWorking;
            _logger.LogInformation("Stopped processing PCS jobs");
        }
    }

    private async Task<Job?> GetJob(QueueClient queueClient, CancellationToken cancellationToken)
    {
        QueueMessage message = await queueClient.ReceiveMessageAsync(visibilityTimeout: TimeSpan.FromSeconds(JobInvisibilityTimeoutSeconds), cancellationToken);
        if (message == null || message.Body == null)
        {
            return null;
        }

        await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);

        return message.Body.ToObjectFromJson<Job>() ?? throw new Exception($"Failed to deserialize {message.Body} into a {nameof(Job)}");
    }

    private void ProcessJob(Job job)
    {
        switch (job)
        {
            case TextJob textJob:
                _logger.LogInformation("Processed text job. Message: {message}", textJob.Text);
                break;
            default:
                throw new NotSupportedException($"Unable to process unknown PCS job type: {job.GetType().Name}");
        }
    }
}
