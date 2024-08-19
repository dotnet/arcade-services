// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Options;
using ProductConstructionService.WorkItems;
using ProductConstructionService.WorkItems.WorkItemDefinitions;

namespace ProductConstructionService.Api.WorkItems;

internal class WorkItemConsumer(
    ILogger<WorkItemConsumer> logger,
    IOptions<WorkItemConsumerOptions> options,
    WorkItemScopeManager scopeManager,
    QueueServiceClient queueServiceClient)
    : BackgroundService
{
    private readonly ILogger<WorkItemConsumer> _logger = logger;
    private readonly IOptions<WorkItemConsumerOptions> _options = options;
    private readonly WorkItemScopeManager _scopeManager = scopeManager;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // We yield so that the rest of the service can progress initialization
        // Otherwise, the service will be stuck here
        await Task.Yield();

        QueueClient queueClient = queueServiceClient.GetQueueClient(_options.Value.WorkItemQueueName);
        _logger.LogInformation("Starting to process PCS queue {queueName}", _options.Value.WorkItemQueueName);
        while (!cancellationToken.IsCancellationRequested)
        {
            using (WorkItemScope workItemScope = _scopeManager.BeginWorkItemScopeWhenReady())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                try
                {
                    await ReadAndProcessWorkItemAsync(queueClient, workItemScope, cancellationToken);
                }
                // If the cancellation token gets cancelled, we just want to exit
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An unexpected exception occurred during PCS work item processing");
                }
            }
        }
    }

    private async Task ReadAndProcessWorkItemAsync(QueueClient queueClient, WorkItemScope workItemScope, CancellationToken cancellationToken)
    {
        QueueMessage message = await queueClient.ReceiveMessageAsync(_options.Value.QueueMessageInvisibilityTime, cancellationToken);

        if (message?.Body == null)
        {
            // Queue is empty, wait a bit
            _logger.LogDebug("Queue {queueName} is empty. Sleeping for {sleepingTime} seconds", _options.Value.WorkItemQueueName, (int)_options.Value.QueuePollTimeout.TotalSeconds);
            await Task.Delay(_options.Value.QueuePollTimeout, cancellationToken);
            return;
        }

        var workItem = message.Body.ToObjectFromJson<WorkItem>();

        workItemScope.InitializeScope(workItem);

        try
        {
            _logger.LogInformation("Starting attempt {attemptNumber} for work item {workItemId}, type {workItemType}", message.DequeueCount, workItem.Id, workItem.Type);
            await workItemScope.RunWorkItemAsync(cancellationToken);

            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
        }
        // If the cancellation token gets cancelled, don't retry, just exit without deleting the message, we'll handle it later
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing worki tem {workItemId} attempt {attempt}/{maxAttempts} failed",
                workItem.Id, message.DequeueCount, _options.Value.MaxWorkItemRetries);
            // Let the workItem retry a few times. If it fails a few times, delete it from the queue, it's a bad work item
            if (message.DequeueCount == _options.Value.MaxWorkItemRetries)
            {
                _logger.LogError("Work item {workItemId} has failed {maxAttempts} times. Discarding the message {message} from the queue",
                    workItem.Id, _options.Value.MaxWorkItemRetries, message.Body.ToString());
                await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            }
        }
    }
}
