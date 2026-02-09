// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Data;

namespace BuildInsights.Utilities.Sql;

public static class DataReaderExtensions
{
    public static List<T> ToList<T>(this IDataReader reader)
    {
        var result = new List<T>();
        while (reader.Read())
        {
            result.Add(reader.ToValue<T>());
        }
        return result;
    }
}
