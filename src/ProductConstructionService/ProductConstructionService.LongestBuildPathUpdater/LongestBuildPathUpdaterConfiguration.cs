﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Common;

namespace ProductConstructionService.LongestBuildPathUpdater;

public static class LongestBuildPathUpdaterConfiguration
{
    public static void ConfigureLongestBuildPathUpdater(
        this HostApplicationBuilder builder,
        ITelemetryChannel telemetryChannel)
    {
        builder.RegisterLogging(telemetryChannel);
        builder.AddBuildAssetRegistry();

        builder.Services.AddTransient<LongestBuildPathUpdater>();
    }
}
