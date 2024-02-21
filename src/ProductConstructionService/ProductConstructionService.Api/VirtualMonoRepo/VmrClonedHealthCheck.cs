// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api.VirtualMonoRepo;

public class VmrClonedHealthCheck(JobProcessorScopeManager jobProcessorScopeManager) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (jobProcessorScopeManager.State == JobsProcessorState.WaitingForVmrClone)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("The JobProcessor is waiting for the VMR to be cloned"));
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
