// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Kusto;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProductConstructionService.Api.Configuration;

internal static class DatabaseConfiguration
{
    private static readonly string _kustoConnectionStringKey = $"Kusto:{nameof(KustoClientProviderOptions.QueryConnectionString)}";

    public static void AddBuildAssetRegistry(this WebApplicationBuilder builder, string connectionString)
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

        // inject the Kusto connection string in the Kusto Configuration section
        builder.Configuration[_kustoConnectionStringKey] = builder.Configuration[PcsConfiguration.KustoConnectionString];
        builder.Services.AddKustoClientProvider("Kusto");
    }
}
