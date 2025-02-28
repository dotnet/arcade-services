﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
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
    private readonly IServiceScope _serviceScope;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly List<IDisposable> _scopeDisposables;

    internal WorkItemScope(
        IOptions<WorkItemProcessorRegistrations> processorRegistrations,
        Func<Task> finalizer,
        IServiceScope serviceScope,
        ITelemetryRecorder telemetryRecorder)
    {
        _processorRegistrations = processorRegistrations.Value;
        _finalizer = finalizer;
        _serviceScope = serviceScope;
        _telemetryRecorder = telemetryRecorder;
        _scopeDisposables = [];
    }

    public async ValueTask DisposeAsync()
    {
        await _finalizer();
        foreach (var disposable in _scopeDisposables)
        {
            disposable.Dispose();
        }
        _serviceScope.Dispose();
    }

    public async Task RunWorkItemAsync(JsonNode node, CancellationToken cancellationToken)
    {
        var type = node["type"]!.ToString();

        if (!_processorRegistrations.Processors.TryGetValue(type, out (Type WorkItem, Type Processor) processorType))
        {
            throw new NonRetriableException($"No processor found for work item type {type}");
        }

        IWorkItemProcessor processor = _serviceScope.ServiceProvider.GetKeyedService<IWorkItemProcessor>(type)
            ?? throw new NonRetriableException($"No processor registration found for work item type {type}");

        if (JsonSerializer.Deserialize(node, processorType.WorkItem, WorkItemConfiguration.JsonSerializerOptions) is not WorkItem workItem)
        {
            throw new NonRetriableException($"Failed to deserialize work item of type {type}: {node}");
        }

        var logger = _serviceScope.ServiceProvider.GetRequiredService<ILogger<IWorkItemProcessor>>();
        var telemetryClient = _serviceScope.ServiceProvider.GetRequiredService<TelemetryClient>();

        async Task ProcessWorkItemAsync()
        {
            var operation = telemetryClient.StartOperation<RequestTelemetry>(type);
            _scopeDisposables.Add(operation);
            ITelemetryScope telemetryScope = _telemetryRecorder.RecordWorkItemCompletion(type);
            _scopeDisposables.Add(telemetryScope);
            var loggingScope = logger.BeginScope(processor.GetLoggingContextData(workItem));
            if (loggingScope != null)
            {
                _scopeDisposables.Add(loggingScope);
            }

            try
            {
                logger.LogInformation("Processing work item {type}", type);
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
            catch (Exception)
            {
                operation.Telemetry.Success = false;
                logger.LogError("Failed to process work item {type}", type);
                throw;
            }
        }

        if (processor.GetRedisMutexKey(workItem) is not string mutexKey)
        {
            await ProcessWorkItemAsync();
            return;
        }

        var cache = _serviceScope.ServiceProvider.GetRequiredService<IRedisCacheFactory>();

        IAsyncDisposable? @lock;
        do
        {
            await using (@lock = await cache.TryAcquireLock(mutexKey, TimeSpan.FromHours(1), cancellationToken))
            {
                if (@lock != null)
                {
                    await ProcessWorkItemAsync();
                }
            }
        } while (@lock == null);
    }
}
