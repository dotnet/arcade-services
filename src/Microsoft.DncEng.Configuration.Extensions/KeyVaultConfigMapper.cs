using System;
using System.Text.RegularExpressions;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public static class KeyVaultConfigMapper
    {
        private static readonly Regex VaultReferenceRegex = new Regex(@"\[vault\((?<key>[^])]+)\)\]");

        public static Func<string, string> Create(IConfiguration bootstrapConfiguration)
        {
            var keyVaultUri = bootstrapConfiguration[ConfigurationConstants.KeyVaultUriConfigurationKey];
            var credentials = ServiceConfigurationExtensions.GetAzureTokenCredential(bootstrapConfiguration);
            var client = new SecretClient(new Uri(keyVaultUri), credentials);
            return value =>
            {
                return VaultReferenceRegex.Replace(value, match =>
                {
                    string key = match.Groups["key"].Value;
                    try
                    {
                        return client.GetSecret(key).Value.Value;
                    }
                    catch (RequestFailedException ex)
                    {
                        return $"<error: Unable to retrieve key vault secret '{key}', '{ex.Message}'>";
                    }
                });
            };
        }
    }
}
