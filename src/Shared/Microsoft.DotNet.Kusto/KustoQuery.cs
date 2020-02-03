// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.Kusto
{
    public class KustoQuery
    {
        public KustoQuery()
        {
            Parameters = new List<KustoParameter>();
        }

        public KustoQuery(string text)
        {
            Text = text;
            Parameters = new List<KustoParameter>();
        }

        public KustoQuery(string text, IEnumerable<KustoParameter> parameters)
        {
            Text = text;
            Parameters = parameters.ToList();
        }

        public KustoQuery(string text, params KustoParameter[] parameters)
        {
            Text = text;
            Parameters = parameters.ToList();
        }

        public List<KustoParameter> Parameters { get; }
        public string Text { get; set; }

        public KustoParameter AddParameter(string name, string type)
        {
            var parameter = new KustoParameter(name, type);
            Parameters.Add(parameter);
            return parameter;
        }
    }
}
