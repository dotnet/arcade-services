// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProductConstructionService.WorkItems;

public class WorkItemScope : IAsyncDisposable
{
    private readonly WorkItemProcessorRegistrations _processorRegistrations;
    private readonly Func<Task> _finalizer;
    private readonly IServiceScope _serviceScope;
    private readonly ITelemetryRecorder _telemetryRecorder;

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
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await _finalizer();
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

        using (logger.BeginScope(processor.GetLoggingContextData(workItem)))
        using (ITelemetryScope telemetryScope = _telemetryRecorder.RecordWorkItemCompletion(type))
        {
            var success = await processor.ProcessWorkItemAsync(workItem, cancellationToken);
            if (success)
            {
                telemetryScope.SetSuccess();
            }
        }
    }
}
