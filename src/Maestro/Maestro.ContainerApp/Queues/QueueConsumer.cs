// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace Maestro.ContainerApp.Queues;

public abstract class QueueConsumer<T>
{
    private readonly QueueServiceClient _queueClient;
    private readonly string _queueName;
    private readonly ILogger _logger;

    protected QueueConsumer(QueueServiceClient queueClient, ILogger logger, string queueName)
    {
        _queueClient = queueClient;
        _queueName = queueName;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _queueClient.CreateQueueAsync(_queueName, cancellationToken: cancellationToken);
        QueueClient client = _queueClient.GetQueueClient(_queueName);

        _logger.LogInformation($"Starting queue consumer for queue '{_queueName}'..");

        while (!cancellationToken.IsCancellationRequested)
        {
            QueueMessage message = (await client.ReceiveMessageAsync(cancellationToken: cancellationToken)).Value;

            if (message == null)
            {
                await Task.Delay(1000, cancellationToken);
                continue;
            }

            if (message.DequeueCount > 5)
            {
                _logger.LogError($"Message {message.MessageId} has been dequeued too many times, deleting it");
                await client.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
                continue;
            }

            try
            {
                T item = JsonSerializer.Deserialize<T>(message.Body)
                    ?? throw new InvalidOperationException("Empty message queue received");
                await ProcessItemAsync(item);
                await client.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError($"Error processing queue item: {e}");
            }
        }
    }

    protected abstract Task ProcessItemAsync(T item);
}
