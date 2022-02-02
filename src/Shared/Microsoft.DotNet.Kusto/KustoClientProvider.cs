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
        private readonly IOptionsMonitor<KustoClientProviderOptions> _options;
        private readonly object _updateLock = new object();
        private ICslQueryProvider _kustoQueryProvider;
        private readonly IDisposable _monitor;

        public KustoClientProvider(IOptionsMonitor<KustoClientProviderOptions> options)
        {
            _options = options;
            _monitor = options.OnChange(ClearProviderCache);
        }

        public KustoClientProvider(IOptionsMonitor<KustoClientProviderOptions> options, ICslQueryProvider provider)
        {
            _options = options;
            _kustoQueryProvider = provider;
        }

        private void ClearProviderCache(KustoClientProviderOptions arg1, string arg2)
        {
            lock (_updateLock)
            {
                _kustoQueryProvider = null;
            }
        }

        public ICslQueryProvider GetProvider()
        {
            var value = _kustoQueryProvider;
            if (value != null)
                return value;
            lock (_updateLock)
            {
                value = _kustoQueryProvider;
                if (value != null)
                    return value;

                _kustoQueryProvider = value = KustoClientFactory.CreateCslQueryProvider(_options.CurrentValue.QueryConnectionString);
                return value;
            }
        }

        private string DatabaseName => _options.CurrentValue.Database;

        public async Task<IDataReader> ExecuteKustoQueryAsync(KustoQuery query)
        {
            var client = GetProvider();
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
            _kustoQueryProvider?.Dispose();
            _monitor?.Dispose();
        }
    }
}
