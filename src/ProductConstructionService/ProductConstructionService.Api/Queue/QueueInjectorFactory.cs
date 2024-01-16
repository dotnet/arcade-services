// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;

namespace ProductConstructionService.Api.Queue;

public class QueueInjectorFactory(QueueServiceClient queueServiceClient, string queueName)
{
    private readonly QueueServiceClient _queueServiceClient = queueServiceClient;
    private readonly string _queueName = queueName;

    public QueueInjector<T> Create<T>() => new(_queueServiceClient, _queueName);
}
