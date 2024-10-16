// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public abstract class DependencyFlowUpdateProcessor<TWorkItem> : WorkItemProcessor<TWorkItem> where TWorkItem : DependencyFlowWorkItem
{
    private readonly IRedisMutex _redisMutex;
    private readonly TelemetryClient _telemetryClient;
    private readonly ILogger _logger;

    protected DependencyFlowUpdateProcessor(
        IRedisMutex redisMutex,
        TelemetryClient telemetryClient,
        ILogger logger)
    {
        _redisMutex = redisMutex;
        _telemetryClient = telemetryClient;
        _logger = logger;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        TWorkItem workItem,
        CancellationToken cancellationToken)
    {
        return await _redisMutex.EnterWhenAvailable(
            workItem.UpdaterId,
            async () =>
            {
                using (_logger.BeginScope(GetLoggingScopeData(workItem)))
                {
                    var requestTelemetry = new RequestTelemetry { Name = workItem.GetType().Name };
                    requestTelemetry.Context.Operation.Id = workItem.UpdaterId;

                    var operation = _telemetryClient.StartOperation(requestTelemetry);

                    try
                    {
                        return await ProcessUpdateAsync(workItem, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        _telemetryClient.TrackException(e);
                        throw;
                    }
                    finally
                    {
                        _telemetryClient.StopOperation(operation);
                    }
                }
            });
    }

    protected abstract Task<bool> ProcessUpdateAsync(TWorkItem workItem, CancellationToken cancellationToken);

    protected virtual Dictionary<string, object> GetLoggingScopeData(TWorkItem workItem) => new()
    {
        { "UpdaterId", workItem.UpdaterId }
    };
}
