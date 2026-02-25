// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data;

namespace BuildInsights.KnownIssues;

internal static class DataReaderHelpers
{
    public static DateTimeOffset GetDateTimeOffset(this IDataReader reader, int ordinal) => new(reader.GetDateTime(ordinal));

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

    public static T? GetReaderValue<T>(IDataRecord reader, int ordinal, Func<int, T> get) where T : class
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

        return string.Concat(value.AsSpan(0, size - 3), "...");
    }
}

internal static class DateTimeOffsetExtensions
{
    public static string ToIso8601String(this DateTimeOffset dto) => dto.UtcDateTime.ToString("O");

    public static string ToIso8601String(this DateTime dt) => dt.ToUniversalTime().ToString("O");
}
