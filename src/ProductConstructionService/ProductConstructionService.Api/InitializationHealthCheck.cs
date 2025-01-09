// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api;

internal class InitializationHealthCheck(WorkItemScopeManager workItemProcessorScopeManager) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (await workItemProcessorScopeManager.GetStateAsync() == WorkItemProcessorState.Initializing)
        {
            return HealthCheckResult.Unhealthy("Background worker is waiting for initialization to finish");
        }

        return HealthCheckResult.Healthy();
    }
}
