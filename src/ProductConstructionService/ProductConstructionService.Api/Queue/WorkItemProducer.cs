// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using ProductConstructionService.WorkItems.WorkItemDefinitions;

namespace ProductConstructionService.Api.Queue;

public class WorkItemProducer<T>(QueueServiceClient queueServiceClient, string queueName) where T : WorkItem
{
    private readonly QueueServiceClient _queueServiceClient = queueServiceClient;
    private readonly string _queueName = queueName;

    public async Task<SendReceipt> ProduceWorkItemAsync(T payload)
    {
        var client = _queueServiceClient.GetQueueClient(_queueName);
        return await client.SendMessageAsync(JsonSerializer.Serialize<WorkItem>(payload));
    }
}
