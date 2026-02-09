// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Data;

namespace BuildInsights.Utilities.Sql;

public static class DataRecordExtensions
{
    public static T ToValue<T>(this IDataRecord row)
    {
        var type = typeof(T);
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean:
            case TypeCode.Byte:
            case TypeCode.Char:
            case TypeCode.DateTime:
            case TypeCode.Decimal:
            case TypeCode.Double:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.Int64:
            case TypeCode.SByte:
            case TypeCode.Single:
            case TypeCode.String:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
            case TypeCode.UInt64:
                return (T) Convert.ChangeType(row.GetValue(0), type);
        }
        return row.ToObject<T>();
    }

    public static T ToObject<T>(this IDataRecord row)
    {
        var result = ReflectionHelper<T>.Create();
        for (int i = 0; i < row.FieldCount; i++)
        {
            string name = row.GetName(i);
            object value = row[i];
            if (value == DBNull.Value)
            {
                value = null;
            }
            ReflectionHelper<T>.TrySet(result, name, value);
        }
        return result;
    }
}