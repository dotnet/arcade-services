// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.Kusto
{
    public class KustoDataType
    {
        private KustoDataType(string cslDataType)
        {
            CslDataType = cslDataType;
        }

        public string CslDataType { get; }

        public static readonly KustoDataType String = new KustoDataType("string");
        public static readonly KustoDataType Long = new KustoDataType("long");
        public static readonly KustoDataType Int = new KustoDataType("int");
        public static readonly KustoDataType Boolean = new KustoDataType("bool");
        public static readonly KustoDataType DateTime = new KustoDataType("datetime");
        public static readonly KustoDataType Guid = new KustoDataType("guid");
        public static readonly KustoDataType TimeSpan = new KustoDataType("timespan");
        public static readonly KustoDataType Real = new KustoDataType("real");
    }
}
