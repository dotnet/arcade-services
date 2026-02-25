// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Maestro.Common.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

internal class WorkItemConsumer(
        string consumerId,
        string queueName,
        ILogger<WorkItemConsumer> logger,
        IOptions<WorkItemConsumerOptions> options,
        WorkItemScopeManager scopeManager,
        QueueServiceClient queueServiceClient,
        IMetricRecorder metricRecorder,
        ITelemetryRecorder telemetryRecorder)
    : BackgroundService
{
    private readonly string _consumerId = consumerId;
    private readonly string _queueName = queueName;
    private readonly ILogger<WorkItemConsumer> _logger = logger;
    private readonly IOptions<WorkItemConsumerOptions> _options = options;
    private readonly WorkItemScopeManager _scopeManager = scopeManager;
    private readonly IMetricRecorder _metricRecorder = metricRecorder;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // We yield so that the rest of the service can progress initialization
        // Otherwise, the service will be stuck here
        await Task.Yield();

        QueueClient queueClient = queueServiceClient.GetQueueClient(_queueName);
        _logger.LogInformation("Consumer {consumerId} starting to process PCS queue {queueName}", _consumerId, _queueName);

        while (!cancellationToken.IsCancellationRequested)
        {
            WorkItemScope workItemScope;
            try
            {
                workItemScope = await _scopeManager.BeginWorkItemScopeWhenReadyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Consumer {consumerId} failed to begin work item scope", _consumerId);
                throw;
            }

            await using (workItemScope)
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

        _logger.LogInformation("Consumer {consumerId} stopping processing PCS queue {queueName}", _consumerId, _queueName);
    }

    private async Task ReadAndProcessWorkItemAsync(QueueClient queueClient, WorkItemScope workItemScope, CancellationToken cancellationToken)
    {
        QueueMessage message = await queueClient.ReceiveMessageAsync(_options.Value.QueueMessageInvisibilityTime, cancellationToken);

        if (message?.Body == null)
        {
            // Queue is empty, wait a bit
            _logger.LogDebug("Queue {queueName} is empty. Sleeping for {sleepingTime} seconds", _queueName, (int)_options.Value.QueuePollTimeout.TotalSeconds);
            await Task.Delay(_options.Value.QueuePollTimeout, cancellationToken);
            return;
        }

        string workItemType;
        int? delay;
        JsonNode node;
        try
        {
            node = JsonNode.Parse(message.Body)!;
            workItemType = node["type"]!.ToString();

            delay = node["delay"]?.GetValue<int>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse work item message {message}", message.Body.ToString());
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        TelemetryClient telemetryClient = workItemScope.GetRequiredService<TelemetryClient>();

        using (var operation = telemetryClient.StartOperation<RequestTelemetry>(workItemType))
        using (ITelemetryScope telemetryScope = telemetryRecorder.RecordWorkItemCompletion(
            workItemType,
            message.DequeueCount,
            operation.Telemetry.Context.Operation.Id))
        {
            try
            {
                _logger.LogInformation("Starting attempt {attemptNumber} for {workItemType}", message.DequeueCount, workItemType);
                await workItemScope.RunWorkItemAsync(
                    node,
                    telemetryScope,
                    // We record the delay between the message being queued and the processing start time
                    // We must only measure that once we actually start processing the work item which might mean waiting for lock
                    () => _metricRecorder.QueueMessageReceived(message, delay ?? 0),
                    cancellationToken);
                await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            }
            // If the cancellation token gets cancelled, don't retry, just exit without deleting the message, we'll handle it later
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                operation.Telemetry.Success = false;
                _logger.LogError(ex, "Processing work item {workItemType} attempt {attempt}/{maxAttempts} failed",
                    workItemType, message.DequeueCount, _options.Value.MaxWorkItemRetries);
                // Let the workItem retry a few times. If it fails a few times, delete it from the queue, it's a bad work item
                if (message.DequeueCount == _options.Value.MaxWorkItemRetries || ex is NonRetriableException)
                {
                    _logger.LogError("Work item {type} has failed {maxAttempts} times. Discarding the message {message} from the queue",
                        workItemType, _options.Value.MaxWorkItemRetries, message.Body.ToString());
                    await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
                }
            }
        }
    }
}

internal class NonRetriableException(string message) : Exception(message)
{
}
