// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.Identity;
using BuildInsights.AzureStorage.Cache;
using BuildInsights.Data;
using BuildInsights.GitHub;
using BuildInsights.GitHubGraphQL;
using BuildInsights.ServiceDefaults.Configuration;
using BuildInsights.ServiceDefaults.Configuration.Models;
using BuildInsights.Utilities.AzureDevOps;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Common.Telemetry;
using Maestro.Services.Common;
using Maestro.Services.Common.Cache;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Helix.Client;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ProductConstructionService.WorkItems;

namespace BuildInsights.ServiceDefaults;

public static class BuildInsightsCommonConfiguration
{
    private const string DefaultWorkItemType = "Default";
    private const string SpecialWorkItemType = "Special";

    public static class ConfigurationKeys
    {
        // All secrets loaded from KeyVault will have this prefix
        public const string KeyVaultSecretPrefix = "KeyVaultSecrets:";

        // Secrets coming from the KeyVault
        public const string GitHubAppPrivateKey = $"{KeyVaultSecretPrefix}github-app-private-key";
        public const string GitHubWebHookSecret = $"{KeyVaultSecretPrefix}github-app-webhook-secret";
        public const string HelixToken = $"{KeyVaultSecretPrefix}helix-api-token";

        // Configuration from appsettings.json
        public const string ConnectionStrings = "ConnectionStrings";
        public const string DatabaseConnectionString = $"{ConnectionStrings}:bi-mssql";
        public const string QueuesConnectionName = "bi-queues";
        public const string RedisConnectionString = $"{ConnectionStrings}:bi-redis";
        public const string BlobEndpoint = $"{ConnectionStrings}:bi-blobs";
        public const string AzureDevOpsConfiguration = "AzureDevOps";
        public const string KeyVaultName = "KeyVaultName";
        public const string ManagedIdentityId = "ManagedIdentityClientId";
        public const string GitHubApp = "GitHubApp";
        public const string GitHubGraphQLOptions = "GitHubGraphQLOptions";
        public const string BlobStorage = "BlobStorage";
        public const string Helix = "Helix";
        public const string Kusto = "Kusto";

        public const string WorkItemQueueName = "WorkItemQueueName";
        public const string SpecialWorkItemQueueName = "SpecialWorkItemQueueName";
        public const string WorkItemConsumerCount = "WorkItemConsumerCount";
    }

    public static async Task ConfigureBuildInsightsDependencies(this IHostApplicationBuilder builder, bool addKeyVault)
    {
        // Load shared appsettings files before project-specific configuration
        builder.AddSharedConfiguration();

        bool isDevelopment = builder.Environment.IsDevelopment();

        string? managedIdentityId = builder.Configuration[ConfigurationKeys.ManagedIdentityId];

        // Set up Key Vault access for some secrets
        TokenCredential azureCredential = isDevelopment
            ? new DefaultAzureCredential()
            : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(managedIdentityId));

        if (addKeyVault)
        {
            Uri keyVaultUri = new($"https://{builder.Configuration.GetRequiredValue(ConfigurationKeys.KeyVaultName)}.vault.azure.net/");
            builder.Configuration.AddAzureKeyVault(
                keyVaultUri,
                azureCredential,
                new KeyVaultSecretsWithPrefix(ConfigurationKeys.KeyVaultSecretPrefix));
        }

        var gitHubAppSettings = builder.Configuration.GetSection(ConfigurationKeys.GitHubApp).Get<GitHubAppSettings>()
            ?? throw new Exception("GitHubAppSettings configuration section is missing");
        builder.Services.Configure<AzureDevOpsTokenProviderOptions>(ConfigurationKeys.AzureDevOpsConfiguration, (o, s) => s.Bind(o));
        builder.Services.Configure<GitHubAppSettings>(ConfigurationKeys.GitHubApp, (o, s) => s.Bind(o));
        builder.Services.Configure<HelixSettings>(ConfigurationKeys.Helix, (o, s) =>
        {
            s.Bind(o);
            o.Token = builder.Configuration[ConfigurationKeys.HelixToken]
                ?? throw new Exception("Helix API token is missing from configuration");
        });
        builder.Services.Configure<BlobStorageSettings>(ConfigurationKeys.BlobStorage, (o, s) =>
        {
            s.Bind(o);
            o.ManagedIdentityId = managedIdentityId;

            if (!isDevelopment)
            {
                o.Endpoint ??= builder.Configuration[ConfigurationKeys.BlobEndpoint];
            }
            else
            {
                o.ConnectionString ??= builder.Configuration[ConfigurationKeys.BlobEndpoint];
            }
        });

        builder.AddServiceDefaults();

        // Set up GitHub and Azure DevOps auth
        builder.Services.AddVssConnection();
        builder.Services.AddGitHubClientFactory(
            gitHubAppSettings.AppId,
            builder.Configuration[ConfigurationKeys.GitHubAppPrivateKey]);
        builder.Services.AddGitHubTokenProvider();
        builder.Services.AddGitHub();
        builder.Services.AddGitHubGraphQL(builder.Configuration.GetSection(ConfigurationKeys.GitHubGraphQLOptions));
        builder.Services.TryAddTransient<IInstallationLookup, InMemoryCacheInstallationLookup>();
        builder.Services.TryAddScoped<IRemoteTokenProvider>(sp =>
        {
            var azdoTokenProvider = sp.GetRequiredService<IAzureDevOpsTokenProvider>();
            var gitHubTokenProvider = sp.GetRequiredService<IGitHubTokenProvider>();
            return new RemoteTokenProvider(azdoTokenProvider, new GitHub.GitHubTokenProvider(gitHubTokenProvider));
        });
        builder.Services.AddScoped<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();

        // Set up SQL database
        string databaseConnectionString = builder.Configuration.GetRequiredValue(ConfigurationKeys.DatabaseConnectionString);
        builder.AddSqlDatabase<BuildInsightsContext>(databaseConnectionString, managedIdentityId);

        // Set up Kusto client provider
        builder.Services.AddKustoClientProvider(options =>
        {
            builder.Configuration.GetSection(ConfigurationKeys.Kusto)?.Bind(options);
            options.ManagedIdentityId = managedIdentityId;
        });
        builder.Services.AddTransient<IKustoIngestClientFactory, KustoIngestClientFactory>();

        // Set up Helix API
        builder.Services.AddScoped<IHelixApi>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<HelixSettings>>();
            return new HelixApi(new HelixApiOptions(
                new Uri(options.Value.Endpoint),
                new HelixApiTokenCredential(options.Value.Token)));
        });

        // Set up background queue processing
        var workItemQueueName = builder.Configuration.GetRequiredValue(ConfigurationKeys.WorkItemQueueName);
        var specialWorkItemQueueName = builder.Configuration.GetRequiredValue(ConfigurationKeys.SpecialWorkItemQueueName);
        builder.AddWorkItemQueues(azureCredential, waitForInitialization: false, new()
        {
            { workItemQueueName, (int.Parse(builder.Configuration.GetRequiredValue(ConfigurationKeys.WorkItemConsumerCount)), DefaultWorkItemType) },
            { specialWorkItemQueueName, (1, SpecialWorkItemType) }
        });
        builder.AddWorkItemProducerFactory(ConfigurationKeys.QueuesConnectionName, azureCredential, workItemQueueName, specialWorkItemQueueName);

        // Set up DI
        builder.Services.AddSingleton<Microsoft.Extensions.Internal.ISystemClock, Microsoft.Extensions.Internal.SystemClock>();
        builder.Services.AddSingleton<ExponentialRetry>();
        builder.Services.Configure<ExponentialRetryOptions>(_ => { });
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(builder.Configuration);

        // Set up Redis
        var redisConnectionString = builder.Configuration[ConfigurationKeys.RedisConnectionString]!;
        await builder.AddRedisCache(redisConnectionString, managedIdentityId);

        // Set up telemetry
        builder.AddDataProtection(azureCredential);
        builder.Services.AddTelemetry();
        builder.Services.AddOperationTracking(_ => { });
        builder.Services.AddHttpLogging(
            options =>
            {
                options.LoggingFields =
                    HttpLoggingFields.RequestPath
                    | HttpLoggingFields.RequestQuery
                    | HttpLoggingFields.ResponseStatusCode;
                options.CombineLogs = true;
            });
    }
}
