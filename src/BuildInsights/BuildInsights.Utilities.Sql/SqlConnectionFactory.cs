// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildInsights.Utilities.Sql;

public class SqlConnectionFactory
{
    private readonly IOptionsMonitor<SqlConnectionSettings> _options;

    public SqlConnectionFactory(IOptionsMonitor<SqlConnectionSettings> options)
    {
        _options = options;
    }

    public async Task<SqlConnection> OpenConnectionAsync(string name)
    {
        var options = _options.Get(name);
        var connectionString = SqlConnectionBuilderHelper.BuildConnectionString(options);
        var conn = new SqlConnection(connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        return conn;
    }
}

public static class SqlServiceCollectionExtensions
{
    public static IServiceCollection AddSqlConnection(this IServiceCollection services)
    {
        return services.AddSingleton<SqlConnectionFactory>();
    }
}
