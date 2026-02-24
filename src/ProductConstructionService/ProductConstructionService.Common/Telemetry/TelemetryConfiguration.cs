// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ProductConstructionService.Common.Telemetry;

public static class TelemetryConfiguration
{
    public static void AddTelemetry(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ITelemetryInitializer, TelemetryRoleNameInitializer>();
        builder.Services.AddSingleton<ITelemetryRecorder, TelemetryRecorder>();
    }
}
