// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using ProductConstructionService.WorkItems.WorkItemDefinitions;

namespace ProductConstructionService.WorkItems;

public interface IWorkItemProducerFactory
{
    public IWorkItemProducer<T> Create<T>() where T : WorkItem;
}

public class WorkItemProducerFactory(QueueServiceClient queueServiceClient, string queueName) : IWorkItemProducerFactory
{
    private readonly QueueServiceClient _queueServiceClient = queueServiceClient;
    private readonly string _queueName = queueName;

    public IWorkItemProducer<T> Create<T>() where T : WorkItem
        => new WorkItemProducer<T>(_queueServiceClient, _queueName);
}
