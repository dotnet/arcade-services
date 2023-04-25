// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Internal.Health;

public sealed class AzureTableHealthReportProvider : IHealthReportProvider, IDisposable
{
    private readonly HttpClient _client;
    private readonly IOptionsMonitor<AzureTableHealthReportingOptions> _options;
    private readonly ILogger<AzureTableHealthReportProvider> _logger;
    private readonly ExponentialRetry _retry;
        
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new JsonSerializerOptions {DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull};

    public AzureTableHealthReportProvider(
        IOptionsMonitor<AzureTableHealthReportingOptions> options,
        ILogger<AzureTableHealthReportProvider> logger,
        IHttpClientFactory clientFactory,
        ExponentialRetry retry)
    {
        _options = options;
        _logger = logger;
        _retry = retry;
        if (string.IsNullOrEmpty(options.CurrentValue.WriteSasUri))
        {
            _logger.LogWarning("AzureTableHealth WriteSas is not configured, no status will be written to table");
        }
        _client = clientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("x-ms-version", "2013-08-15");
    }

    private bool TryGetUrlParts(out string baseUrl, out string sasQuery)
    {
        AzureTableHealthReportingOptions options = _options.CurrentValue;
        if (string.IsNullOrEmpty(options.WriteSasUri))
        {
            baseUrl = null;
            sasQuery = null;
            return false;
        }

        var builder = new UriBuilder(options.WriteSasUri);
        sasQuery = builder.Query;
        builder.Query = null;
        baseUrl = builder.ToString();
        return true;
    }

    private static string GetRowKey(string instance, string subStatus) => EscapeKeyField(instance ?? "") + "|" + EscapeKeyField(subStatus);
    private static (string instance, string subStatus) ParseRowKey(string rowKey)
    {
        var parts = rowKey.Split('|');
        var subStatus = UnescapeKeyField(parts[1]);
        if (string.IsNullOrEmpty(parts[0]))
            return (null, subStatus);
        return (UnescapeKeyField(parts[0]), subStatus);
    }

    private static string EscapeKeyField(string value) =>
        value.Replace(":", "\0")
            .Replace("|", ":pipe:")
            .Replace("\\", ":back:")
            .Replace("/", ":slash:")
            .Replace("#", ":hash:")
            .Replace("?", ":question:")
            .Replace("\0", ":colon:");

    private static string UnescapeKeyField(string value) =>
        value.Replace(":colon:", "\0")
            .Replace(":pipe:", "|")
            .Replace(":back:", "\\")
            .Replace(":slash:", "/")
            .Replace(":hash:", "#")
            .Replace(":question:", "?")
            .Replace("\0", ":");

    public async Task UpdateStatusAsync(string serviceName, string instance, string subStatusName, HealthStatus status, string message)
    {
        if (!TryGetUrlParts(out string baseUrl, out string sasQuery))
            return;

        string partitionKey = EscapeKeyField(serviceName);
        string rowKey = GetRowKey(instance, subStatusName);

        async Task Attempt()
        {
            HttpContent content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(
                new Entity {Status = status, Message = message},
                s_jsonSerializerOptions
            ));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            string requestUri =
                $"{baseUrl}(PartitionKey='{Uri.EscapeDataString(partitionKey)}',RowKey='{Uri.EscapeDataString(rowKey)}'){sasQuery}";
            using HttpResponseMessage response = await _client.PutAsync(requestUri, content).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        try
        {
            await _retry.RetryAsync(
                Attempt,
                e => _logger.LogWarning("Failed to update status for {service}/{subStatus}, retrying",
                    serviceName,
                    subStatusName),
                e => true
            ).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Crashing out a service trying to report health isn't useful, log that we failed and move on
            _logger.LogError(e, "Unable to update health status for {service}/{subStatus}", serviceName, subStatusName);
        }
    }

    public Task<IList<HealthReport>> GetAllStatusAsync(string serviceName)
    {
        if (!TryGetUrlParts(out string baseUrl, out string sasQuery))
            return Task.FromResult<IList<HealthReport>>(Array.Empty<HealthReport>());

        string partitionKey = EscapeKeyField(serviceName);
        string filter = $"PartitionKey eq '{partitionKey}'";

        async Task<IList<HealthReport>> Attempt()
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}(){sasQuery}&$filter={Uri.EscapeDataString(filter)}"
            );

            request.Headers.Accept.ParseAdd("application/json;odata=nometadata");

            using HttpResponseMessage response = await _client.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Array.Empty<HealthReport>();

            response.EnsureSuccessStatusCode();
            var entities = JsonSerializer.Deserialize<ValueList<Entity>>(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
            return entities.Value.Select(
                e =>
                {
                    var (instance, subStatus) = ParseRowKey(e.RowKey);
                    return new HealthReport(
                        serviceName,
                        instance,
                        subStatus,
                        e.Status,
                        e.Message,
                        e.Timestamp.Value
                    );
                }
            ).ToList();
        }

        return _retry.RetryAsync(
            Attempt,
            e => _logger.LogWarning(e, "Failed to fetch status, trying again"),
            _ => true);
    }

    private class ValueList<T>
    {
        [JsonPropertyName("value")]
        public T[] Value { get; set; }
    }

    private class Entity
    {
        public DateTimeOffset? Timestamp { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HealthStatus Status { get; set; }
        public string Message { get; set; }
        public string RowKey { get; set; }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
