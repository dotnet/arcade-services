// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;

namespace Maestro.ContainerApp.Queues;

public class QueueProducerFactory
{
    private readonly QueueServiceClient _queueClient;

    public QueueProducerFactory(QueueServiceClient queueClient)
    {
        _queueClient = queueClient;
    }

    public QueueProducer<T> Create<T>(string queueName) => new(_queueClient, queueName);
}
