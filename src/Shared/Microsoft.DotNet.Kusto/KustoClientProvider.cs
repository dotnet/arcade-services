// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;

namespace Microsoft.DotNet.Kusto
{
    public class KustoClientProvider : IKustoClientProvider
    {
        private readonly IOptions<KustoClientProviderOptions> _options;

        public KustoClientProvider(IOptions<KustoClientProviderOptions> options)
        {
            _options = options;
        }

        public KustoClientProviderOptions Options => _options.Value;

        public ICslQueryProvider GetKustoQueryConnectionProvider()
        {
            return KustoClientFactory.CreateCslQueryProvider(Options.QueryConnectionString);
        }
        public string GetKustoDatabase()
        {
            return Options.Database;
        }
    }
}
