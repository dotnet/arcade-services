// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Kusto.Data.Common;
using Kusto.Data.Exceptions;
using Kusto.Data.Net.Client;
using Microsoft.Extensions.Options;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Kusto
{
    public class KustoClientProvider : IKustoClientProvider
    {
        private readonly IOptions<KustoClientProviderOptions> _options;
        private readonly ICslQueryProvider _kustoQueryProvider;

        public KustoClientProvider(IOptions<KustoClientProviderOptions> options)
        {
            _options = options;
            _kustoQueryProvider = KustoClientFactory.CreateCslQueryProvider(options.Value.QueryConnectionString);
        }

        public ICslQueryProvider KustoQueryProvider => _kustoQueryProvider;

        public string DatabaseName => _options.Value.Database;

        public async Task<T> GetSingleValueFromQueryAsync<T>(KustoQuery query)
        {
            var result = await ExecuteKustoQueryAsync(query);

            if (result.Read())
            {
                var resultValue = result.GetValue(0);
                if (resultValue == System.DBNull.Value || resultValue == null)
                {
                    return default(T);
                }
                else
                {
                    return (T) resultValue;
                }
            }

            return default(T);
        }


        public async Task<IDataReader> ExecuteKustoQueryAsync(KustoQuery query)
        {
            var client = KustoQueryProvider;
            var properties = new ClientRequestProperties();
            foreach (var parameter in query.Parameters)
            {
                properties.SetParameter(parameter.Name, parameter.Value.ToString());
            }

            string text = query.Text;
            if (query.Parameters?.Any() == true)
            {
                string parameterList = String.Join(",", query.Parameters.Select(p => $"{p.Name}:{p.Type}"));
                text = $"declare query_parameters ({parameterList});{query.Text}";
            }

            try
            {
                return await client.ExecuteQueryAsync(
                    DatabaseName,
                    text,
                    properties);
            }
            catch (SemanticException)
            {
                return null;
            }
        }
    }
}
