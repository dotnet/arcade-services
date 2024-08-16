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

namespace ProductConstructionService.SubscriptionTriggerer;

public static class SubscriptionTriggererConfiguration
{
    private const string ApplicationInsightsConnectionString = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    private const string AspnetcoreEnvironment = "ASPNETCORE_ENVIRONMENT";
    private const string QueueConnectionString = "QueueConnectionString";
    private const string ManagedIdentityClientId = "ManagedIdentityClientId";
    private const string SqlConnectionStringUserIdPlaceholder = "USER_ID_PLACEHOLDER";
    private const string DatabaseConnectionString = "BuildAssetRegistrySqlConnectionString";

    public static HostApplicationBuilder ConfigureSubscriptionTriggerer(
        this HostApplicationBuilder builder,
        ITelemetryChannel telemetryChannel,
        bool isDevelopment)
    {
        RegisterLogging(builder.Services, telemetryChannel, isDevelopment);

        string databaseConnectionString = builder.Configuration.GetRequiredValue(DatabaseConnectionString)
            .Replace(SqlConnectionStringUserIdPlaceholder, builder.Configuration[ManagedIdentityClientId]);

        builder.Services.AddBuildAssetRegistry((provider, options) =>
        {
            options.UseSqlServerWithRetry(databaseConnectionString);
        });

        builder.Services.Configure<OperationManagerOptions>(o => { });
        builder.Services.Configure<ConsoleLifetimeOptions>(o => { });
        builder.Services.AddTransient<OperationManager>();

        builder.Services.AddTransient<DarcRemoteMemoryCache>();
        builder.Services.AddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
        builder.Services.AddTransient<IVersionDetailsParser, VersionDetailsParser>();
        builder.Services.AddTransient<IBasicBarClient, SqlBarClient>();
        builder.Services.AddTransient(_ => new QueueClient(
            new Uri(builder.Configuration.GetRequiredValue(QueueConnectionString)),
            new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = builder.Configuration[ManagedIdentityClientId]
            })));
        builder.Services.AddKustoClientProvider("Kusto");

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

public static class ConfigurationExtension
{
    // Environment is set with the DOTNET_ENVIRONMENT env variable
    public static string GetRequiredValue(this IConfiguration config, string key) =>
        config[key] ?? throw new ArgumentException($"{key} missing from the configuration / environment settings");
}
