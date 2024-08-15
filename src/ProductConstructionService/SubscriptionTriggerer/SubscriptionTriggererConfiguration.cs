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
using Microsoft.Identity.Client.AppConfig;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.DotNet.Internal.Logging;

namespace SubscriptionTriggerer;
public class SubscriptionTriggererConfiguration
{
    private const string ApplicationInsightsConnectionString = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    private const string AspnetcoreEnvironment = "ASPNETCORE_ENVIRONMENT";
    private const string QueueConnectionString = "QueueConnectionString";
    private const string ManagedIdentityClientId = "ManagedIdentityClientId";
    private const string SqlConnectionStringUserIdPlaceholder = "USER_ID_PLACEHOLDER";
    private const string DatabaseConnectionString = "BuildAssetRegistrySqlConnectionString";

    public static IServiceCollection RegisterServices(IServiceCollection services, ITelemetryChannel telemetryChannel)
    {
        RegisterLogging(services, telemetryChannel);

        IConfiguration config = GetConfiguration();
        services.AddSingleton(_ => config);

        string databaseConnectionString = config.GetRequiredValue(DatabaseConnectionString)
            .Replace(SqlConnectionStringUserIdPlaceholder, config[ManagedIdentityClientId]);

        services.AddBuildAssetRegistry((provider, options) =>
        {
            options.UseSqlServerWithRetry(databaseConnectionString);
        });

        services.Configure<OperationManagerOptions>(o => { });
        services.AddTransient<OperationManager>();

        services.AddTransient<IHostEnvironment>(_ => new HostEnvironment());
        services.AddTransient<DarcRemoteMemoryCache>();
        services.AddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
        services.AddTransient<IVersionDetailsParser, VersionDetailsParser>();
        services.AddTransient<IBasicBarClient, SqlBarClient>();
        services.AddTransient(_ => new QueueClient(
            new Uri(config.GetRequiredValue(QueueConnectionString)),
            new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = config[ManagedIdentityClientId]
            })));
        services.AddKustoClientProvider("Kusto");

        return services;
    }

    private static IConfiguration GetConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable(AspnetcoreEnvironment);
        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true);
        return builder.Build();
    }

    private static IServiceCollection RegisterLogging(IServiceCollection services, ITelemetryChannel telemetryChannel)
    {
        services.Configure<TelemetryConfiguration>(
            config =>
            {
                config.ConnectionString = Environment.GetEnvironmentVariable(ApplicationInsightsConnectionString);
                config.TelemetryChannel = telemetryChannel;
            }
        );

        services.AddLogging(builder =>
        {
            builder.AddApplicationInsights();
            // Console logging will be useful if we're investigating Console logs of a single job run
            builder.AddConsole();
        });
        return services;
    }
}

public static class ConfigurationExtension
{
    public static string GetRequiredValue(this IConfiguration config, string key) =>
        config[key] ?? throw new ArgumentException($"{key} missing from the configuration / environment settings");
}

// BuildAssetRegistryContext needs an IHostEnvironment, but I'm pretty sure it's not used at all.
// I'd like to leave this as is to see if this is correct
internal class HostEnvironment : IHostEnvironment
{
    public string ApplicationName { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public IFileProvider ContentRootFileProvider { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string ContentRootPath { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string EnvironmentName { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
}
