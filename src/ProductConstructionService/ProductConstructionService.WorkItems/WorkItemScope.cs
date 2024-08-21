// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.WorkItems;

internal class WorkItemScope(
        WorkItemProcessorRegistrations processorRegistrations,
        Action finalizer,
        IServiceScope serviceScope,
        ITelemetryRecorder telemetryRecorder,
        ILogger logger)
    : IDisposable
{
    private readonly WorkItemProcessorRegistrations _processorRegistrations = processorRegistrations;
    private readonly IServiceScope _serviceScope = serviceScope;
    private readonly ITelemetryRecorder _telemetryRecorder = telemetryRecorder;
    private readonly ILogger _logger = logger;

    public void Dispose()
    {
        finalizer.Invoke();
        _serviceScope.Dispose();
    }

    public async Task RunWorkItemAsync(JsonNode node, CancellationToken cancellationToken)
    {
        var type = node["type"]!.ToString();

        if (!_processorRegistrations.Processors.TryGetValue(type, out (Type WorkItem, Type Processor) processorType))
        {
            throw new NonRetriableException($"No processor found for work item type {type}");
        }

        var processor = _serviceScope.ServiceProvider.GetService(processorType.Processor)
            ?? throw new NonRetriableException($"No processor registration found for work item type {type}");

        if (JsonSerializer.Deserialize(node, processorType.WorkItem) is not WorkItem workItem)
        {
            throw new NonRetriableException($"Failed to deserialize work item of type {type}: {node}");
        }

        using (ITelemetryScope telemetryScope = _telemetryRecorder.RecordWorkItemCompletion(type))
        {
            var method = processorType.Processor.GetMethod(nameof(IWorkItemProcessor<WorkItem>.ProcessWorkItemAsync));
            var success = await (Task<bool>)method!.Invoke(processor, [workItem, cancellationToken])!;
            if (success)
            {
                telemetryScope.SetSuccess();
            }
        }
    }
}
