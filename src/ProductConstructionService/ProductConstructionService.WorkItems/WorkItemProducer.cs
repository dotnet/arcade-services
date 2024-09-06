// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace ProductConstructionService.WorkItems;

public interface IWorkItemProducer<T>
{
    /// <summary>
    /// Puts a WorkItem into the queue, which becomes visible after the specified delay.
    /// </summary>
    Task<SendReceipt> ProduceWorkItemAsync(T payload, TimeSpan delay = default);

    /// <summary>
    /// Deletes a WorkItem from the queue.
    /// </summary>
    Task DeleteWorkItemAsync(string messageId, string popReceipt);
}

public class WorkItemProducer<T>(QueueServiceClient queueServiceClient, string queueName) : IWorkItemProducer<T> where T : WorkItem
{
    private readonly QueueServiceClient _queueServiceClient = queueServiceClient;
    private readonly string _queueName = queueName;

    public async Task<SendReceipt> ProduceWorkItemAsync(T payload, TimeSpan delay = default)
    {
        var client = _queueServiceClient.GetQueueClient(_queueName);

        if (delay != default)
        {
            payload.Delay = delay;
        }

        var json = JsonSerializer.Serialize(payload, WorkItemConfiguration.JsonSerializerOptions);
        return await client.SendMessageAsync(json, delay);
    }

    public async Task DeleteWorkItemAsync(string messageId, string popReceipt)
    {
        var client = _queueServiceClient.GetQueueClient(_queueName);
        await client.DeleteMessageAsync(messageId, popReceipt);
    }
}
