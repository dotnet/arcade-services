// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using Maestro.ContainerApp.Controllers;

namespace Maestro.ContainerApp.Queues;

internal class SubscriptionQueueProcessor : QueueConsumer<StartSubscriptionUpdateWorkItem>
{
    private readonly ILogger _logger;

    public SubscriptionQueueProcessor(QueueServiceClient queueClient, ILogger<SubscriptionQueueProcessor> logger)
        : base(queueClient, logger, QueueConfiguration.SubscriptionTriggerQueueName)
    {
        _logger = logger;
    }

    protected override async Task ProcessItemAsync(StartSubscriptionUpdateWorkItem item)
    {
        _logger.LogInformation($"Executing subscription trigger for {item.Id}");
        await Task.Delay(1000);
        _logger.LogInformation($"Subscription {item.Id} triggered");
    }
}
