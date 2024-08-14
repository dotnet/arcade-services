// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProductConstructionService.Api.Configuration;

internal static class DatabaseConfiguration
{
    public static void AddBuildAssetRegistry(this WebApplicationBuilder builder, string connectionString, string? managedIdentityId)
    {
        builder.Services.TryAddTransient<IBasicBarClient, SqlBarClient>();
        builder.Services.AddDbContext<BuildAssetRegistryContext>(options =>
        {
            // Do not log DB context initialization
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ContextInitialized));

            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
            });
        });

        // If we're using a user assigned managed identity, inject it into the Kusto configuration section
        if (!string.IsNullOrEmpty(managedIdentityId))
        {
            string kustoManagedIdentityIdKey = $"Kusto:{nameof(KustoOptions.ManagedIdentityId)}";
            builder.Configuration[kustoManagedIdentityIdKey] = managedIdentityId;
        }

        builder.Services.AddKustoClientProvider("Kusto");
        builder.Services.AddSingleton<IInstallationLookup, BuildAssetRegistryInstallationLookup>();
    }
}
