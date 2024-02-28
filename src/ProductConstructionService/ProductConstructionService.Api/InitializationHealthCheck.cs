﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api;

public class InitializationHealthCheck(JobScopeManager jobProcessorScopeManager) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (jobProcessorScopeManager.State == JobsProcessorState.Initializing)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Background worker is waiting for initialization to finish"));
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
