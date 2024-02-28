// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProductConstructionService.Api;

internal static class DatabaseConfiguration
{
    public static void AddBuildAssetRegistry(this WebApplicationBuilder builder, string connectionString)
    {
        builder.Services.TryAddTransient<IBasicBarClient, SqlBarClient>();
        builder.Services.AddDbContext<BuildAssetRegistryContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
            });
        });
    }
}
