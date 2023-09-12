// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using Maestro.ContainerApp.Queues.WorkItems;

namespace Maestro.ContainerApp.Queues;

public class QueueProducerFactory
{
    private readonly QueueServiceClient _queueClient;
    private readonly string _queueName;

    public QueueProducerFactory(QueueServiceClient queueClient, string queueName)
    {
        _queueClient = queueClient;
        _queueName = queueName;
    }

    public QueueProducer<T> Create<T>() where T : BackgroundWorkItem
        => new(_queueClient, _queueName);
}
