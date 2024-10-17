// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public abstract class DependencyFlowUpdateProcessor<TWorkItem> : WorkItemProcessor<TWorkItem> where TWorkItem : DependencyFlowWorkItem
{
    private readonly IRedisMutex _redisMutex;
    private readonly TelemetryClient _telemetryClient;

    protected DependencyFlowUpdateProcessor(
        IRedisMutex redisMutex,
        TelemetryClient telemetryClient)
    {
        _redisMutex = redisMutex;
        _telemetryClient = telemetryClient;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        TWorkItem workItem,
        CancellationToken cancellationToken)
    {
        return await _redisMutex.EnterWhenAvailable(workItem.UpdaterId,
            async () =>
            {
                using (var operation = _telemetryClient.StartOperation<RequestTelemetry>(workItem.GetType().Name))
                {
                    try
                    {
                        return await ProcessUpdateAsync(workItem, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        _telemetryClient.TrackException(e);
                        throw;
                    }
                }
            });
    }

    protected abstract Task<bool> ProcessUpdateAsync(TWorkItem workItem, CancellationToken cancellationToken);

    protected override Dictionary<string, object> GetLoggingContextData(TWorkItem workItem)
    {
        var data = base.GetLoggingContextData(workItem);
        data["UpdaterId"] = workItem.UpdaterId;
        return data;
    }
}
