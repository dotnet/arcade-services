// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using ProductConstructionService.Jobs.Jobs;

namespace ProductConstructionService.Api.Queue;

public class JobProducerFactory(QueueServiceClient queueServiceClient, string queueName)
{
    private readonly QueueServiceClient _queueServiceClient = queueServiceClient;
    private readonly string _queueName = queueName;

    public JobProducer<T> Create<T>() where T : Job
        => new(_queueServiceClient, _queueName);
}
