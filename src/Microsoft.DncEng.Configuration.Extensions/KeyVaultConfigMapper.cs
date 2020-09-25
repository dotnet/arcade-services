using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public static class KeyVaultConfigMapper
    {
        private static readonly Regex VaultReferenceRegex = new Regex(@"\[vault\((?<key>[^])]+)\)\]");

        public static Func<string, string> Create(IConfiguration bootstrapConfiguration)
        {
            TelemetryClient telemetry = ConfigMapper.GetTelemetryClient(bootstrapConfiguration);
            string keyVaultUri = bootstrapConfiguration[ConfigurationConstants.KeyVaultUriConfigurationKey];
            var credentials = ServiceConfigurationExtensions.GetAzureTokenCredential(bootstrapConfiguration);
            var client = new SecretClient(new Uri(keyVaultUri), credentials);

            void TrackEvent(string key, Exception? error)
            {
                telemetry.TrackEvent("KeyVaultSecretAccess", new Dictionary<string, string>
                {
                    ["secretKey"] = key,
                    ["keyVaultUri"] = keyVaultUri,
                    ["success"] = error == null ? "true" : "false",
                });
                if (error != null)
                {
                    telemetry.TrackException(error);
                }
            }

            return RegexConfigMapper.Create(VaultReferenceRegex, key =>
            {
                try
                {
                    var value = client.GetSecret(key).Value.Value;
                    TrackEvent(key, null);
                    return value;
                }
                catch (RequestFailedException ex)
                {
                    TrackEvent(key, ex);
                    return "";
                }
            });
        }
    }
}
