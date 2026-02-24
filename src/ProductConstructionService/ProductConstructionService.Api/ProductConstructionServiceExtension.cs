// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api;

public static class ProductConstructionServiceExtension
{
    public const string ManagedIdentityClientId = "ManagedIdentityClientId";
    private const string SqlConnectionStringUserIdPlaceholder = "USER_ID_PLACEHOLDER";
    private const string DatabaseConnectionString = "BuildAssetRegistrySqlConnectionString";

    public static void AddBuildAssetRegistry(this IHostApplicationBuilder builder)
    {
        var managedIdentityClientId = builder.Configuration[ManagedIdentityClientId];
        string databaseConnectionString = builder.Configuration.GetRequiredValue(DatabaseConnectionString)
            .Replace(SqlConnectionStringUserIdPlaceholder, managedIdentityClientId);

        builder.Services.AddDbContext<BuildAssetRegistryContext>(options =>
        {
            // Do not log DB context initialization and command executed events
            options.ConfigureWarnings(w =>
            {
                w.Ignore(CoreEventId.ContextInitialized);
                w.Ignore(RelationalEventId.CommandExecuted);
            });

            options.UseSqlServer(databaseConnectionString, sqlOptions =>
            {
                sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
            });
        });

        // If we're using a user assigned managed identity, inject it into the Kusto configuration section
        if (!string.IsNullOrEmpty(managedIdentityClientId))
        {
            string kustoManagedIdentityIdKey = $"Kusto:{nameof(KustoOptions.ManagedIdentityId)}";
            builder.Configuration[kustoManagedIdentityIdKey] = managedIdentityClientId;
        }

        builder.Services.AddKustoClientProvider("Kusto");
        builder.Services.AddSingleton<IInstallationLookup, BuildAssetRegistryInstallationLookup>();
    }

    public static void AddMetricRecorder(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IMetricRecorder, MetricRecorder>();
    }
}
