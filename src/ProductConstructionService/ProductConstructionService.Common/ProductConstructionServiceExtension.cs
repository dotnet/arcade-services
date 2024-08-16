// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using Maestro.Data;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ProductConstructionService.Common;

public static class ProductConstructionServiceExtension
{
    private const string QueueConnectionString = "QueueConnectionString";
    private const string ManagedIdentityClientId = "ManagedIdentityClientId";
    private const string SqlConnectionStringUserIdPlaceholder = "USER_ID_PLACEHOLDER";
    private const string DatabaseConnectionString = "BuildAssetRegistrySqlConnectionString";

    public static void RegisterCommonServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool skipBarRegistration = false,
        bool skipQueueRegistration = false)
    {
        if (!skipBarRegistration)
        {
            services.RegisterBuildAssetRegistry(configuration);
        }

        if (!skipQueueRegistration)
        {
            services.AddTransient(_ => new QueueClient(
                new Uri(configuration.GetRequiredValue(QueueConnectionString)),
                new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = configuration[ManagedIdentityClientId]
                })));
        }
    }

    public static void RegisterBuildAssetRegistry(this IServiceCollection services, IConfiguration configuration)
    {
        var managedIdentityClientId = configuration[ManagedIdentityClientId];
        string databaseConnectionString = configuration.GetRequiredValue(DatabaseConnectionString)
            .Replace(SqlConnectionStringUserIdPlaceholder, managedIdentityClientId);

        services.TryAddTransient<IBasicBarClient, SqlBarClient>();
        services.AddDbContext<BuildAssetRegistryContext>(options =>
        {
            // Do not log DB context initialization
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ContextInitialized));

            options.UseSqlServer(databaseConnectionString, sqlOptions =>
            {
                sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
            });
        });

        // If we're using a user assigned managed identity, inject it into the Kusto configuration section
        if (!string.IsNullOrEmpty(managedIdentityClientId))
        {
            string kustoManagedIdentityIdKey = $"Kusto:{nameof(KustoOptions.ManagedIdentityId)}";
            configuration[kustoManagedIdentityIdKey] = managedIdentityClientId;
        }

        services.AddKustoClientProvider("Kusto");
        services.AddSingleton<IInstallationLookup, BuildAssetRegistryInstallationLookup>(); ;
    }
}
