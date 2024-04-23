﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;

namespace ProductConstructionService.Api.Telemetry;

public static class TelemetryConfiguration
{
    public static void AddTelemetry(this WebApplicationBuilder builder)
    {
        builder.Services.AddApplicationInsightsTelemetry();
        builder.Services.AddApplicationInsightsTelemetryProcessor<RemoveDefaultPropertiesTelemetryProcessor>();
        builder.Services.AddSingleton<ITelemetryRecorder, TelemetryRecorder>();     
    }
}
