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
    public sealed class KustoClientProvider : IKustoClientProvider, IDisposable
    {
        private readonly IOptions<KustoClientProviderOptions> _options;
        public ICslQueryProvider KustoQueryProvider { get; }
        
        public KustoClientProvider(IOptions<KustoClientProviderOptions> options)
        {
            _options = options;
            KustoQueryProvider = KustoClientFactory.CreateCslQueryProvider(options.Value.QueryConnectionString);
        }

        public KustoClientProvider(IOptions<KustoClientProviderOptions> options, ICslQueryProvider provider)
        {
            _options = options;
            KustoQueryProvider = provider;
        }

        public string DatabaseName => _options.Value.Database;

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
                string parameterList = string.Join(",", query.Parameters.Select(p => $"{p.Name}:{p.Type.CslDataType}"));
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

        public void Dispose()
        {
            KustoQueryProvider.Dispose();
        }
    }
}
