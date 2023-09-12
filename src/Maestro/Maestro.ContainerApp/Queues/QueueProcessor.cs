// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.ContainerApp.Queues;

internal class QueueProcessor : BackgroundService
{
    private readonly SubscriptionQueueProcessor _subscriptionQueueProcessor;
    private readonly ILogger _logger;

    public QueueProcessor(SubscriptionQueueProcessor subscriptionQueueProcessor, ILogger<QueueProcessor> logger)
    {
        _subscriptionQueueProcessor = subscriptionQueueProcessor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _subscriptionQueueProcessor.StartAsync(stoppingToken);
    }
}
