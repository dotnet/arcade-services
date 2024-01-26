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
    JobsProcessorStatus status,
    QueueServiceClient queueServiceClient)
    : BackgroundService
{
    private readonly ILogger<JobsProcessor> _logger = logger;
    private readonly IOptions<JobProcessorOptions> _options = options;
    private readonly JobsProcessorStatus _status = status;

    protected override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => ProcessJobs(cancellationToken), cancellationToken);
    }

    private async Task ProcessJobs(CancellationToken cancellationToken)
    {
        QueueClient queueClient = queueServiceClient.GetQueueClient(_options.Value.JobQueueName);
        _logger.LogInformation("Starting to process PCS jobs {queueName}", _options.Value.JobQueueName);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _status.WaitIfStoppingAsync(cancellationToken);

                QueueMessage message = await queueClient.ReceiveMessageAsync(_options.Value.QueueMessageInvisibilityTime, cancellationToken);

                if (message?.Body == null)
                {
                    // Queue is empty, wait a bit
                    _logger.LogInformation("Queue {queueName} is empty. Sleeping for {sleepingTime} seconds", _options.Value.JobQueueName, (int)_options.Value.EmptyQueueWaitTime.TotalSeconds);
                    await Task.Delay(_options.Value.EmptyQueueWaitTime, cancellationToken);
                    continue;
                }

                var job = message.Body.ToObjectFromJson<Job>();

                try
                {
                    ProcessJob(job);

                    await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Processing job {jobId} attempt {attempt}/{maxAttempts} failed",
                        ex, job.Id, message.DequeueCount, _options.Value.MaxJobRetries);
                    // Let the job retry a few times. If it fails a few times, delete it from the queue, it's a bad job
                    if (message.DequeueCount == _options.Value.MaxJobRetries)
                    {
                        await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
                    }
                }
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "An unexpected exception {exception} occurred", ex);
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
