// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Internal.Health
{
    public sealed class AzureTableHealthReportProvider : IHealthReportProvider, IDisposable
    {
        private readonly HttpClient _client;
        private readonly ILogger<AzureTableHealthReportProvider> _logger;
        private readonly string _sasQuery;
        private readonly string _baseUrl;

        public AzureTableHealthReportProvider(
            IOptions<AzureTableHealthReportingOptions> options,
            ILogger<AzureTableHealthReportProvider> logger,
            IHttpClientFactory clientFactory)
        {
            _logger = logger;
            UriBuilder builder = new UriBuilder(options.Value.WriteSasUri);
            _sasQuery = builder.Query;
            builder.Query = null;
            _baseUrl = builder.ToString();

            _client = clientFactory.CreateClient();
            _client.DefaultRequestHeaders.Add("x-ms-version", "2019-12-12");
        }

        public async Task UpdateStatusAsync(string serviceName, string subStatusName, HealthStatus status, string message)
        {
            HttpContent content = new StringContent(JsonConvert.SerializeObject(new
            {
                Status = status.ToString(),
                Message = message,
            }));

            try
            {
                using HttpResponseMessage response = await _client.PutAsync(
                    $"{_baseUrl}(PartitionKey='{Uri.EscapeDataString(serviceName)}',RowKey='{Uri.EscapeDataString(subStatusName)}'){_sasQuery}",
                    content
                );
                response.EnsureSuccessStatusCode();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to update health status for {service}/{subStatus}", serviceName, subStatusName);
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
