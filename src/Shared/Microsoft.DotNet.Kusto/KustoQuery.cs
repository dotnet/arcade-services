// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.Kusto
{
    public class KustoQuery
    {
        public KustoQuery(string text = null, IEnumerable<KustoParameter> parameters = null, IEnumerable<KustoQueryOption> options = null)
        {
            Text = text;
            Parameters = parameters?.ToList() ?? new List<KustoParameter>();
            Options = options?.ToList() ?? new List<KustoQueryOption>();
        }

        public KustoQuery(string text) : this(text, new List<KustoParameter>())
        {
        }

        public KustoQuery(string text, params KustoParameter[] parameters) : this(text, (IEnumerable<KustoParameter>) parameters)
        {
        }

        public List<KustoParameter> Parameters { get; }
        public string Text { get; set; } 

        public void AddParameter(string name, object value, KustoDataType type)
        {
            Parameters.Add(new KustoParameter(name, value, type));
        }
    }
}
