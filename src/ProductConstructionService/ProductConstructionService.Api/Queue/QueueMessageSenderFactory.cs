// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using ProductConstructionService.Api.Queue.WorkItems;

namespace ProductConstructionService.Api.Queue;

public class QueueMessageSenderFactory(QueueServiceClient queueServiceClient, string queueName)
{
    private readonly QueueServiceClient _queueServiceClient = queueServiceClient;
    private readonly string _queueName = queueName;

    public QueueMessageSender<T> Create<T>() where T : WorkItem 
        => new(_queueServiceClient, _queueName);
}
