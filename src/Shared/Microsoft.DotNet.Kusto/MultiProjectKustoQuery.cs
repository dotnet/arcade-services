// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.Kusto
{
    public class MultiProjectKustoQuery
    {
        public MultiProjectKustoQuery(KustoQuery internalQuery, KustoQuery publicQuery)
        {
            Internal = internalQuery;
            Public = publicQuery;
        }

        public KustoQuery Internal { get; set; }
        public KustoQuery Public { get; set; }
    }
}