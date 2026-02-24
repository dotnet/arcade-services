// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ProductConstructionService.Common;

public static class DatabaseExtensions
{
    private const string SqlConnectionStringUserIdPlaceholder = "USER_ID_PLACEHOLDER";

    public static void AddSqlDatabase<TContext>(
        this IHostApplicationBuilder builder,
        string databaseConnectionString,
        string? managedIdentityId)
        where TContext : DbContext
    {
        if (!string.IsNullOrEmpty(managedIdentityId))
        {
            databaseConnectionString = databaseConnectionString
                .Replace(SqlConnectionStringUserIdPlaceholder, managedIdentityId);
        }

        builder.Services.AddDbContext<TContext>(options =>
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
    }
}
