// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data;
using System.Data.SqlClient;
using System.Reflection;

namespace BuildInsights.Utilities.Sql;

public static class SqlHelper
{
    public static SqlCommand CreateCommand(this SqlTransaction txn)
    {
        var command = txn.Connection.CreateCommand();
        command.Transaction = txn;
        return command;
    }

    public static async Task<List<T>> ExecuteListAsync<T>(this SqlCommand command)
    {
        using (var reader = await command.ExecuteReaderAsync())
        {
            var result = new List<T>();
            while (await reader.ReadAsync())
            {
                result.Add(reader.ToObject<T>());
            }
            return result;
        }
    }

    public static async Task<int> ExecuteBasicCommandAsync(this SqlTransaction txn, string commandText)
    {
        using (var command = txn.Connection.CreateCommand())
        {
            command.Transaction = txn;
            command.CommandText = commandText;
            return await command.ExecuteNonQueryAsync();
        }
    }

    public static SqlParameter WithValue(this SqlParameter that, object value)
    {
        that.Value = value;
        return that;
    }

    public static DateTimeOffset GetDateTimeOffset(this IDataReader reader, int ordinal) => new DateTimeOffset(reader.GetDateTime(ordinal));

    public static object GetDbValue<T>(T value) where T : class
    {
        if (value == null)
        {
            return DBNull.Value;
        }

        return value;
    }

    public static object GetNullableDbValue<T>(T? value) where T : struct
    {
        if (!value.HasValue)
        {
            return DBNull.Value;
        }

        return value.Value;
    }

    public static T GetReaderValue<T>(SqlDataReader reader, int ordinal, Func<int, T> get) where T : class
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }
        return get(ordinal);
    }

    public static T? GetNullableReaderValue<T>(SqlDataReader reader, int ordinal, Func<int, T> get) where T : struct
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }
        return get(ordinal);
    }

    public static T GetReaderValue<T>(IDataRecord reader, int ordinal, Func<int, T> get) where T : class
    {
        // Kusto IsDBNull doesn't work
        if (reader.IsDBNull(ordinal) || reader.GetValue(ordinal) == DBNull.Value || reader.GetValue(ordinal) == null)
        {
            return null;
        }
        return get(ordinal);
    }

    public static T? GetNullableReaderValue<T>(IDataRecord reader, int ordinal, Func<int, T> get) where T : struct
    {
        // Kusto IsDBNull doesn't work
        if (reader.IsDBNull(ordinal) || reader.GetValue(ordinal) == DBNull.Value || reader.GetValue(ordinal) == null)
        {
            return null;
        }
        return get(ordinal);
    }

    public static object TruncateString(string value, int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size), "Size must be greater than zero");

        if (value == null)
        {
            return DBNull.Value;
        }

        if (value.Length <= size)
        {
            return value;
        }

        if (size < 5)
        {
            // "a..." isn't super useful to truncate everything to, so don't add the elipsis for "extra short" things
            return value.Substring(0, size);
        }

        return value.Substring(0, size - 3) + "...";
    }

    public static void AddCollectionParameters<T>(SqlCommand command, IEnumerable<T> values, SqlDbType dbType, out string paramList)
    {
        int count = command.Parameters.Count;
        List<string> paramNames = [];
        foreach (var value in values)
        {
            count++;
            string name = "param" + count;
            command.Parameters.Add(name, dbType).Value = value;
            paramNames.Add($"@{name}");
        }

        paramList = string.Join(",", paramNames);
    }

    public static void AddCollectionParameters<T>(SqlCommand command, IEnumerable<T> values, SqlDbType dbType, int size, out List<string> paramNames)
    {
        int count = command.Parameters.Count;
        paramNames = [];
        foreach (var value in values)
        {
            count++;
            string name = "param" + count;
            command.Parameters.Add(name, dbType, size).Value = value;
            paramNames.Add($"@{name}");
        }
    }

    public static void AddCollectionParameters<T>(SqlCommand command, IEnumerable<T> values, SqlDbType dbType, int size, out string paramList)
    {
        AddCollectionParameters(command, values, dbType, size, out List<string> paramNames);
        paramList = string.Join(",", paramNames);
    }

    public static T GetReaderValue<T>(SqlDataReader reader, string name, Func<int, T> get) where T : class
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }
        return get(ordinal);
    }

    public static T? GetNullableReaderValue<T>(SqlDataReader reader, string name, Func<int, T> get) where T : struct
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }
        return get(ordinal);
    }

    public static T GetRequiredReaderValue<T>(SqlDataReader reader, string name, Func<int, T> get) where T : struct
    {
        var ordinal = reader.GetOrdinal(name);
        return get(ordinal);
    }
}

public static class ReflectionHelper<T>
{
    private static TypeInfo TypeInfo { get; } = typeof(T).GetTypeInfo();
    private static readonly Dictionary<string, Action<T, object>> s_setters = [];
    private static readonly Action<T, object> s_noop = (t, v) => { };

    private static Action<T, object> GetSetter(string property)
    {
        Action<T, object> setter;
        if (s_setters.TryGetValue(property, out setter))
        {
            return setter;
        }
        else
        {
            return s_setters[property] = GetSetterFn(property);
        }
    }

    private static Action<T, object> GetSetterFn(string property)
    {
        var propertyInfo = TypeInfo.GetProperty(property);
        if (propertyInfo == null)
        {
            return s_noop;
        }
        var method = typeof(ReflectionHelper<T>).GetMethod("GetSetterDelegate",
            BindingFlags.NonPublic | BindingFlags.Static);
        var propertyType = propertyInfo.PropertyType;
        var invocableMethod = method.MakeGenericMethod(propertyType);
        var fn = (Delegate)invocableMethod.Invoke(null, new object[]
        {
            property
        });
        return (t, v) =>
        {
            fn.DynamicInvoke(t, v);
        };
    }

    public static T Create()
    {
        return Activator.CreateInstance<T>();
    }

    public static void TrySet<TValue>(T obj, string property, TValue value)
    {
        GetSetter(property)(obj, value);
    }
}
