// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.DataProviders;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.DotNet.Kusto;
using Azure.Storage.Queues;
using Azure.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.DotNet.Internal.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.SubscriptionTriggerer;

public static class SubscriptionTriggererConfiguration
{
    private const string ApplicationInsightsConnectionString = "APPLICATIONINSIGHTS_CONNECTION_STRING";  

    public static HostApplicationBuilder ConfigureSubscriptionTriggerer(
        this HostApplicationBuilder builder,
        ITelemetryChannel telemetryChannel,
        bool isDevelopment)
    {
        RegisterLogging(builder.Services, telemetryChannel, isDevelopment);

        builder.Services.RegisterCommonServices(builder.Configuration);

        builder.Services.Configure<OperationManagerOptions>(o => { });
        builder.Services.Configure<ConsoleLifetimeOptions>(o => { });
        builder.Services.AddTransient<OperationManager>();

        builder.Services.AddTransient<DarcRemoteMemoryCache>();
        builder.Services.AddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
        builder.Services.AddTransient<IVersionDetailsParser, VersionDetailsParser>();

        builder.Services.AddTransient<SubscriptionTriggerer>();

        return builder;
    }

    private static IServiceCollection RegisterLogging(
        IServiceCollection services,
        ITelemetryChannel telemetryChannel,
        bool isDevelopment)
    {
        if (!isDevelopment)
        {
            services.Configure<TelemetryConfiguration>(
                config =>
                {
                    config.ConnectionString = Environment.GetEnvironmentVariable(ApplicationInsightsConnectionString);
                    config.TelemetryChannel = telemetryChannel;
                }
            );
        }

        services.AddLogging(builder =>
        {
            if (!isDevelopment)
            {
                builder.AddApplicationInsights();
            }
            // Console logging will be useful if we're investigating Console logs of a single job run
            builder.AddConsole();
        });
        return services;
    }
}
