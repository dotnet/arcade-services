// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Options;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue;

public class JobsProcessor(
    ILogger<JobsProcessor> logger,
    IOptions<JobProcessorOptions> options,
    JobsProcessorScopeManager scopeManager,
    QueueServiceClient queueServiceClient)
    : BackgroundService
{
    private readonly ILogger<JobsProcessor> _logger = logger;
    private readonly IOptions<JobProcessorOptions> _options = options;
    private readonly JobsProcessorScopeManager _scopeManager = scopeManager;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // The API won't be initialized until the BackgroundService goes async. Since the scopeManagers blocks aren't async, we have to do it here
        await Task.Delay(1000);

        QueueClient queueClient = queueServiceClient.GetQueueClient(_options.Value.JobQueueName);
        _logger.LogInformation("Starting to process PCS jobs {queueName}", _options.Value.JobQueueName);
        while (!cancellationToken.IsCancellationRequested)
        {
            using (_scopeManager.BeginJobScopeWhenReady())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                try
                {
                    await ReadAndProcessJobAsync(queueClient, cancellationToken);
                }
                // If the cancellation token gets cancelled, we just want to exit
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An unexpected exception occurred during Pcs job processing");
                }
            }
        }
    }

    private async Task ReadAndProcessJobAsync(QueueClient queueClient, CancellationToken cancellationToken)
    {
        QueueMessage message = await queueClient.ReceiveMessageAsync(_options.Value.QueueMessageInvisibilityTime, cancellationToken);

        if (message?.Body == null)
        {
            // Queue is empty, wait a bit
            _logger.LogDebug("Queue {queueName} is empty. Sleeping for {sleepingTime} seconds", _options.Value.JobQueueName, (int)_options.Value.QueuePollTimeout.TotalSeconds);
            await Task.Delay(_options.Value.QueuePollTimeout, cancellationToken);
            return;
        }

        var job = message.Body.ToObjectFromJson<Job>();

        try
        {
            _logger.LogInformation("Starting attempt {attemptNumber} for job {jobId}, type {jobType}", message.DequeueCount, job.Id, job.GetType());
            ProcessJob(job);

            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing job {jobId} attempt {attempt}/{maxAttempts} failed",
                job.Id, message.DequeueCount, _options.Value.MaxJobRetries);
            // Let the job retry a few times. If it fails a few times, delete it from the queue, it's a bad job
            if (message.DequeueCount == _options.Value.MaxJobRetries)
            {
                _logger.LogError("Job {jobId} has failed {maxAttempts} times. Discarding the message {message} from the queue"
                    , job.Id, _options.Value.MaxJobRetries, message.Body.ToString());
                await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            }
        }
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
