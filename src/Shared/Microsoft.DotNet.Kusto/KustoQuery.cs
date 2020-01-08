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
