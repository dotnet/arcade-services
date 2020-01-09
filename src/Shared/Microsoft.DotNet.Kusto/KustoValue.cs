// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.Kusto
{
    public struct KustoValue
    {
        public KustoValue(string column, string stringValue, string dataType)
        {
            Column = column;
            StringValue = stringValue;
            DataType = dataType;
        }

        public string Column { get; }
        public string StringValue { get; }
        public string DataType { get; }
    }
}
