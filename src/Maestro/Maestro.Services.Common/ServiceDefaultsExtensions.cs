// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Maestro.Services.Common;
/// <summary>
///
/// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
/// This project should be referenced by each service project in your solution.
/// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
/// </summary>
public static class ServiceDefaultsExtensions
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";

    public static IHostApplicationBuilder AddServiceDefaults(
        this IHostApplicationBuilder builder,
        params string[] customMetrics)
    {
        builder.ConfigureOpenTelemetry(customMetrics);
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    private static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live")
        });

        return app;
    }

    private static IHostApplicationBuilder ConfigureOpenTelemetry(
        this IHostApplicationBuilder builder,
        params string[] customMetrics)
    {
        builder.AddDefaultOpenTelemetryLogging();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                var meters = metrics.AddRuntimeInstrumentation()
                       .AddBuiltInMeters();

                foreach (var customMeter in customMetrics)
                {
                    meters.AddMeter(customMeter);
                }
            })
            .WithTracing(tracing =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    // We want to view all traces in development
                    tracing.SetSampler(new AlwaysOnSampler());
                }

                tracing.AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                       .AddGrpcClientInstrumentation()
                       .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters(customMetrics);

        return builder;
    }

    private static IHostApplicationBuilder AddDefaultOpenTelemetryLogging(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder, params string[] customMetrics)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry();
            builder.Services.Configure<OpenTelemetryLoggerOptions>(logging => logging.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddOtlpExporter());

            foreach (var metric in customMetrics)
            {
                builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(metric));
            }
        }

        // This method call will read the APPLICATIONINSIGHTS_CONNECTION_STRING environmental variable, and
        // setup the Application Insights logging
        // We don't need this locally
        builder.AddAzureMonitorOpenTelemetryExporter();

        return builder;
    }

    private static IHostApplicationBuilder AddAzureMonitorOpenTelemetryExporter(this IHostApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            builder.Services
                .AddOpenTelemetry()
                .UseAzureMonitor(options =>
                {
                    // Disable trace-based log sampling to ensure all logs (including Information/Debug) are sent.
                    // This was changed in Azure.Monitor.OpenTelemetry.Exporter 1.5.0 which filters logs by default.
                    options.EnableTraceBasedLogsSampler = false;
                });
        }

        return builder;
    }

    private static MeterProviderBuilder AddBuiltInMeters(this MeterProviderBuilder meterProviderBuilder) =>
        meterProviderBuilder.AddMeter(
            "Microsoft.AspNetCore.Hosting",
            "Microsoft.AspNetCore.Server.Kestrel",
            "System.Net.Http");
}
