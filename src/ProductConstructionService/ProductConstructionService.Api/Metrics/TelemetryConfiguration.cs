// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Metrics;

public static class TelemetryConfiguration
{
    public static void AddTelemetry(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ITelemetryRecorder, TelemetryRecorder>();
        builder.Services.AddApplicationInsightsTelemetry();
        builder.Services.AddApplicationInsightsTelemetryProcessor<RemoveDefaultPropertiesTelemetryProcessor>();
    }
}
