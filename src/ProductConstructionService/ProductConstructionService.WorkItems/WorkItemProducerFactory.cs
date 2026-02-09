// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;

namespace ProductConstructionService.WorkItems;

public interface IWorkItemProducerFactory
{
    public IWorkItemProducer<T> CreateProducer<T>(bool IsSpecialQueue = false) where T : WorkItem;
}

public class WorkItemProducerFactory(QueueServiceClient queueServiceClient, string defaultQueueName, string specialQueueName) : IWorkItemProducerFactory
{
    private readonly QueueServiceClient _queueServiceClient = queueServiceClient;
    private readonly string _defaultQueueName = defaultQueueName;
    private readonly string _specialQueueName = specialQueueName;

    public IWorkItemProducer<T> CreateProducer<T>(bool isCodeFlowSubscription = false) where T : WorkItem
        => isCodeFlowSubscription
            ? new WorkItemProducer<T>(_queueServiceClient, _specialQueueName)
            : new WorkItemProducer<T>(_queueServiceClient, _defaultQueueName);
}
