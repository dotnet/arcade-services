// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DotNet.Kusto
{
    public readonly struct KustoValue
    {
        public KustoValue(string column, object value, KustoDataType dataType)
        {
            Column = column;
            DataType = dataType;
            StringValue = GetCslRepresentation(value);
        }

        private static string GetCslRepresentation(object value)
        {
            switch (value)
            {
                case null:
                    return "";
                case double d:
                    return d.ToString("G17");
                case float f:
                    return f.ToString("G9");
                case DateTime dt:
                    switch (dt.Kind)
                    {
                        case DateTimeKind.Unspecified:
                            throw new ArgumentException("date time must have kind specified, Local or Utc", nameof(value));
                        case DateTimeKind.Utc:
                            return dt.ToString("O");
                        case DateTimeKind.Local:
                            return dt.ToUniversalTime().ToString("O");
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                case DateTimeOffset dto:
                    return dto.UtcDateTime.ToString("O");
                default:
                    return value.ToString() ?? "";
            }
        }

        public string Column { get; }
        public string StringValue { get; }
        public KustoDataType DataType { get; }
    }
}
