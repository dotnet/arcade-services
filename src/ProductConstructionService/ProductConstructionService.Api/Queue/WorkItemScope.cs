// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using ProductConstructionService.WorkItems.WorkItemDefinitions;
using ProductConstructionService.WorkItems.WorkItemProcessors;

namespace ProductConstructionService.Api.Queue;

public class WorkItemScope(
        Action finalizer,
        IServiceScope serviceScope,
        ITelemetryRecorder telemetryRecorder)
    : IDisposable
{
    private readonly IServiceScope _serviceScope = serviceScope;
    private readonly ITelemetryRecorder _telemetryRecorder = telemetryRecorder;
    private WorkItem? _workItem = null;

    public void Dispose()
    {
        finalizer.Invoke();
        _serviceScope.Dispose();
    }

    public void InitializeScope(WorkItem workItem)
    {
        _workItem = workItem;
    }

    public async Task RunWorkItemAsync(CancellationToken cancellationToken)
    {
        if (_workItem is null)
        {
            throw new Exception($"{nameof(WorkItemScope)} not initialized! Call InitializeScope before calling {nameof(RunWorkItemAsync)}");
        }

        var jobRunner = _serviceScope.ServiceProvider.GetRequiredKeyedService<IWorkItemProcessor>(_workItem.Type);

        using (ITelemetryScope telemetryScope = _telemetryRecorder.RecordWorkItemCompletion(_workItem.Type))
        {
            await jobRunner.ProcessWorkItemAsync(_workItem, cancellationToken);
            telemetryScope.SetSuccess();
        }
    }
}
