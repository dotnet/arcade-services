// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Internal.Health
{
    public sealed class AzureTableHealthReportProvider : IHealthReportProvider, IDisposable
    {
        private readonly HttpClient _client;
        private readonly ILogger<AzureTableHealthReportProvider> _logger;
        private readonly string _sasQuery;
        private readonly string _baseUrl;
        private static readonly JsonSerializerOptions s_jsonSerializerOptions = new JsonSerializerOptions {IgnoreNullValues = true};

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
            _client.DefaultRequestHeaders.Add("x-ms-version", "2013-08-15");
        }

        public async Task UpdateStatusAsync(string serviceName, string subStatusName, HealthStatus status, string message)
        {
            async Task Attempt()
            {
                HttpContent content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(
                    new Entity {Status = status, Message = message},
                    s_jsonSerializerOptions
                ));
                content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

                string requestUri =
                    $"{_baseUrl}(PartitionKey='{Uri.EscapeDataString(serviceName)}',RowKey='{Uri.EscapeDataString(subStatusName)}'){_sasQuery}";
                using HttpResponseMessage response = await _client.PutAsync(requestUri, content);
                response.EnsureSuccessStatusCode();
            }

            try
            {
                await ExponentialRetry.RetryAsync(
                    Attempt,
                    e => _logger.LogWarning("Failed to update status for {service}/{subStatus}, retrying",
                        serviceName,
                        subStatusName),
                    e => true
                );
            }
            catch (Exception e)
            {
                // Crashing out a service trying to report health isn't useful, log that we failed and move on
                _logger.LogError(e, "Unable to update health status for {service}/{subStatus}", serviceName, subStatusName);
            }
        }

        public async Task<HealthReport> GetStatusAsync(string serviceName, string subStatusName)
        {
            async Task<HealthReport> Attempt()
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{_baseUrl}(PartitionKey='{Uri.EscapeDataString(serviceName)}',RowKey='{Uri.EscapeDataString(subStatusName)}'){_sasQuery}"
                );

                request.Headers.Accept.ParseAdd("application/json;odata=nometadata");

                using HttpResponseMessage response = await _client.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new HealthReport(serviceName,
                        subStatusName,
                        HealthStatus.Unknown,
                        "",
                        DateTimeOffset.MinValue);

                response.EnsureSuccessStatusCode();

                var entity = JsonSerializer.Deserialize<Entity>(await response.Content.ReadAsByteArrayAsync());
                return new HealthReport(
                    serviceName,
                    subStatusName,
                    entity.Status,
                    entity.Message,
                    entity.Timestamp.Value
                );
            }

            return await ExponentialRetry.RetryAsync(
                Attempt,
                e => _logger.LogWarning(e, "Failed to fetch status, trying again"),
                _ => true);
        }

        private class Entity
        {
            public DateTimeOffset? Timestamp { get; set; }
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public HealthStatus Status { get; set; }
            public string Message { get; set; }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }

    public class HealthReport
    {
        public HealthReport(string serviceName, string subStatusName, HealthStatus health, string message, DateTimeOffset asOf)
        {
            ServiceName = serviceName;
            SubStatusName = subStatusName;
            Health = health;
            Message = message;
            AsOf = asOf;
        }

        public string ServiceName { get; }
        public string SubStatusName { get; }
        public HealthStatus Health { get; }
        public string Message { get; }
        public DateTimeOffset AsOf { get; }
    }
}
