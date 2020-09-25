using System;
using System.Runtime.CompilerServices;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public static class ConfigMapper
    {
        private static readonly ConditionalWeakTable<IConfiguration, TelemetryClient> Clients = new ConditionalWeakTable<IConfiguration, TelemetryClient>();
        public static TelemetryClient GetTelemetryClient(IConfiguration config)
        {
            if (!Clients.TryGetValue(config, out var client))
            {
                client = CreateTelemetryClient();
                Clients.Add(config, client);
            }

            return client;
        }

        private static TelemetryClient CreateTelemetryClient()
        {
            var config = new TelemetryConfiguration(GetApplicationInsightsKey());
            return new TelemetryClient(config);
        }

        private static string GetApplicationInsightsKey()
        {
            string? envVar = Environment.GetEnvironmentVariable("APPLICATION_INSIGHTS_KEY");
            if (string.IsNullOrEmpty(envVar))
            {
                return Guid.Empty.ToString("D");
            }

            return envVar;
        }
    }
}
