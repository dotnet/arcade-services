// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maestro.Common.Telemetry;

public static class TelemetryConfiguration
{
    public static void AddTelemetry(this IServiceCollection services)
    {
        services.TryAddSingleton<ITelemetryInitializer, TelemetryRoleNameInitializer>();
        services.TryAddSingleton<ITelemetryRecorder, TelemetryRecorder>();
        services.TryAddSingleton<IMetricRecorder, MetricRecorder>();
    }
}
