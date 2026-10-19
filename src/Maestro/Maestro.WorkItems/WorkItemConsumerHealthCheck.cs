// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maestro.WorkItems;

internal sealed class WorkItemConsumerHealthCheck(
    IEnumerable<IHostedService> hostedServices,
    IHostApplicationLifetime applicationLifetime,
    ILogger<WorkItemConsumerHealthCheck> logger) : IHealthCheck
{
    private readonly WorkItemConsumer[] _consumers = hostedServices.OfType<WorkItemConsumer>().ToArray();

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!applicationLifetime.ApplicationStarted.IsCancellationRequested ||
            applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }

        string[] stoppedConsumers = _consumers
            .Where(consumer => consumer.ExecuteTask is null || consumer.ExecuteTask.IsCompleted)
            .Select(consumer => $"{consumer.ConsumerId} ({consumer.QueueName})")
            .ToArray();

        if (stoppedConsumers.Length == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        else
        {
            string consumers = string.Join(", ", stoppedConsumers);
            logger.LogError("Work item consumers are not running: {Consumers}", consumers);
            return Task.FromResult(HealthCheckResult.Unhealthy($"Work item consumers are not running: {consumers}"));
        }
    }
}
