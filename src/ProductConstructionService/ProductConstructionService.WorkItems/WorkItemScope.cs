// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public class WorkItemScope : IAsyncDisposable
{
    private readonly WorkItemProcessorRegistrations _processorRegistrations;
    private readonly Func<Task> _finalizer;
    private readonly IServiceScope _workItemScope;

    internal WorkItemScope(
        IOptions<WorkItemProcessorRegistrations> processorRegistrations,
        Func<Task> finalizer,
        IServiceScope serviceScope)
    {
        _processorRegistrations = processorRegistrations.Value;
        _finalizer = finalizer;
        _workItemScope = serviceScope;
    }

    public async ValueTask DisposeAsync()
    {
        await _finalizer();
        _workItemScope.Dispose();
    }

    public async Task RunWorkItemAsync(
        JsonNode node,
        ITelemetryScope telemetryScope,
        Action onWorkItemStarted,
        CancellationToken cancellationToken)
    {
        var type = node["type"]!.ToString();

        if (!_processorRegistrations.Processors.TryGetValue(type, out (Type WorkItem, Type Processor) processorType))
        {
            throw new NonRetriableException($"No processor found for work item type {type}");
        }

        IWorkItemProcessor processor = _workItemScope.ServiceProvider.GetKeyedService<IWorkItemProcessor>(type)
            ?? throw new NonRetriableException($"No processor registration found for work item type {type}");

        if (JsonSerializer.Deserialize(node, processorType.WorkItem, WorkItemConfiguration.JsonSerializerOptions) is not WorkItem workItem)
        {
            throw new NonRetriableException($"Failed to deserialize work item of type {type}: {node}");
        }

        var logger = _workItemScope.ServiceProvider.GetRequiredService<ILogger<IWorkItemProcessor>>();

        async Task ProcessWorkItemAsync()
        {
            onWorkItemStarted();

            using (logger.BeginScope(processor.GetLoggingContextData(workItem)))
            {
                var success = await processor.ProcessWorkItemAsync(workItem, cancellationToken);
                if (success)
                {
                    telemetryScope.SetSuccess();
                    logger.LogInformation("Work item {type} processed successfully", type);
                }
                else
                {
                    logger.LogInformation("Work item {type} processed unsuccessfully", type);
                }
            }
        }

        // If the processor does not require a mutex, just process the work item
        if (processor.GetRedisMutexKey(workItem) is not string mutexKey)
        {
            await ProcessWorkItemAsync();
            return;
        }

        // Otherwise, acquire a mutex and process it under the lock
        var cache = _workItemScope.ServiceProvider.GetRequiredService<IRedisCacheFactory>();
        var stopwatch = Stopwatch.StartNew();

        IAsyncDisposable? @lock;
        do
        {
            await using (@lock = await cache.TryAcquireLock(mutexKey, TimeSpan.FromHours(1), cancellationToken))
            {
                if (@lock != null)
                {
                    stopwatch.Stop();
                    logger.LogInformation("Acquired lock for {type} in {elapsedMilliseconds} ms",
                        type,
                        (int)stopwatch.ElapsedMilliseconds);
                    await ProcessWorkItemAsync();
                }
            }
        } while (@lock == null);
    }

    public T GetRequiredService<T>() where T : notnull
    {
        return _workItemScope.ServiceProvider.GetRequiredService<T>();
    }
}
