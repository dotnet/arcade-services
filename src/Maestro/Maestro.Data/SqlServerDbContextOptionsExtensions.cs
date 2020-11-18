// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;

namespace Maestro.Data
{
    public static class SqlServerDbContextOptionsExtensions
    {
        // Summary:
        //     Configures the context to connect to a Microsoft SQL Server database using EnableRetryOnFailure option
        //     see: https://docs.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency
        // Parameters:
        //   optionsBuilder:
        //     The builder being used to configure the context.
        //
        //   connectionString:
        //     The connection string of the database to connect to.
        //
        //   sqlServerOptionsAction:
        //     An optional action to allow additional SQL Server specific configuration.
        //
        // Returns:
        //     The options builder so that further configuration can be chained.
        public static DbContextOptionsBuilder UseSqlServerWithRetry([NotNullAttribute] this DbContextOptionsBuilder optionsBuilder, [NotNullAttribute] string connectionString, [CanBeNullAttribute] Action<SqlServerDbContextOptionsBuilder> sqlServerOptionsAction = null)
        {
            return optionsBuilder.UseSqlServer(connectionString, opts =>
            {
                opts.EnableRetryOnFailure();
                if (sqlServerOptionsAction != null)
                {
                    sqlServerOptionsAction(opts);
                }
            });
        }
    }
}
