// Licensed to the .NET Foundation under one or more agreements.
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
        ITelemetryChannel telemetryChannel,
        bool isDevelopment)
    {
        builder.Services.RegisterLogging(telemetryChannel, isDevelopment);
        builder.Services.RegisterBuildAssetRegistry(builder.Configuration);

        builder.Services.Configure<ConsoleLifetimeOptions>(o => { });

        builder.Services.AddTransient<LongestBuildPathUpdater>();
    }
}
