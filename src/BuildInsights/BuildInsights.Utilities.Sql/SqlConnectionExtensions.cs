// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data.SqlClient;

namespace BuildInsights.Utilities.Sql;

public static class SqlConnectionExtensions
{
    public static IAsyncEnumerable<T> ExecuteAsyncReader<T>(this SqlConnection connection, string query)
    {
        return ExecuteAsyncReader<T>(connection, query, -1);
    }

    public static IAsyncEnumerable<T> ExecuteAsyncReader<T>(this SqlConnection connection, string query, int commandTimeoutInSeconds)
    {
        return AsyncEnumerableExtensions.Create<T>(async yield =>
        {
            using (var command = connection.CreateCommand())
            {
                if (commandTimeoutInSeconds > 0)
                {
                    command.CommandTimeout = commandTimeoutInSeconds;
                }
                command.CommandText = query;
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        yield(reader.ToValue<T>());
                    }
                }
            }
        });
    }

    public static async Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(this SqlConnection connection, string query)
    {
        return await ExecuteReaderAsync<T>(connection, query, -1);
    }

    public static async Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(this SqlConnection connection, string query, int commandTimeoutInSeconds)
    {
        using (var command = connection.CreateCommand())
        {
            if (commandTimeoutInSeconds > 0)
            {
                command.CommandTimeout = commandTimeoutInSeconds;
            }
            command.CommandText = query;
            using (var reader = await command.ExecuteReaderAsync())
            {
                return reader.ToList<T>();
            }
        }
    }

    public static async Task<int> ExecuteNonQueryAsync(this SqlConnection connection, string query)
    {
        return await ExecuteNonQueryAsync(connection, query, -1);
    }

    public static async Task<int> ExecuteNonQueryAsync(this SqlConnection connection, string query, int commandTimeoutInSeconds)
    {
        using (var command = connection.CreateCommand())
        {
            if (commandTimeoutInSeconds > 0)
            {
                command.CommandTimeout = commandTimeoutInSeconds;
            }
            command.CommandText = query;
            return await command.ExecuteNonQueryAsync();
        }
    }
}
