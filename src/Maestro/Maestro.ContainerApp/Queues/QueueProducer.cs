// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Azure.Storage.Queues;

namespace Maestro.ContainerApp.Queues;

public class QueueProducer<T>
{
    private readonly QueueServiceClient _queueClient;
    private readonly string _queueName;

    public QueueProducer(QueueServiceClient queueClient, string queueName)
    {
        _queueClient = queueClient;
        _queueName = queueName;
    }

    public async Task SendAsync(T message)
    {
        var client = _queueClient.GetQueueClient(_queueName);
        var json = JsonSerializer.Serialize(message);
        await client.SendMessageAsync(json);
    }
}
