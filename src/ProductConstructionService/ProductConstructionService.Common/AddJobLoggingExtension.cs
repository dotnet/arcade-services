// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.Common;
public static class AddJobLoggingExtension
{
    private const string ApplicationInsightsConnectionString = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>
    /// Extension method used to register logging for ConsoleApplications running in Container Jobs
    /// </summary>
    /// <param name="telemetryChannel">Telemetry channel where logs will be buffered. Needs to be flushed at the end of the app</param>
    /// <returns></returns>
    public static IServiceCollection RegisterLogging(
        this IServiceCollection services,
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
