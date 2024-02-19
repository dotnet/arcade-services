// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Options;
using ProductConstructionService.Api.Queue.Jobs;
using ProductConstructionService.Api.Telemetry;

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
            using (JobScope jobScope = _scopeManager.BeginJobScopeWhenReady())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                try
                {
                    await ReadAndProcessJobAsync(queueClient, jobScope, cancellationToken);
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

    private async Task ReadAndProcessJobAsync(QueueClient queueClient, JobScope jobScope, CancellationToken cancellationToken)
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

        jobScope.InitializeScope(job);

        try
        {
            _logger.LogInformation("Starting attempt {attemptNumber} for job {jobId}, type {jobType}", message.DequeueCount, job.Id, job.Type);
            await jobScope.RunJobAsync(cancellationToken);

            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
        }
        // If the cancellation token gets cancelled, don't retry, just exit without deleting the message, we'll handle it later
        catch (OperationCanceledException)
        {
            throw;
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
}
