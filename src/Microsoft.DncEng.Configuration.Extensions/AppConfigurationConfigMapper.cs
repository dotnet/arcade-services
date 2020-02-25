using System;
using System.Text.RegularExpressions;
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public static class AppConfigurationConfigMapper
    {
        private static readonly Regex ConfigReferenceRegex = new Regex(@"\[config\((?<key>[^])]+)\)\]");

        public static Func<string, string> Create(IConfiguration bootstrapConfiguration)
        {
            var appConfigurationUri = bootstrapConfiguration[ConfigurationConstants.AppConfigurationUriConfigurationKey];
            if (appConfigurationUri == null)
            {
                return value => value;
            }
            var credentials = ServiceConfigurationExtensions.GetAzureTokenCredential(bootstrapConfiguration);
            var client = new ConfigurationClient(new Uri(appConfigurationUri), credentials);
            return value =>
            {
                return ConfigReferenceRegex.Replace(value, match =>
                {
                    string key = match.Groups["key"].Value;
                    try
                    {
                        return client.GetConfigurationSetting(key).Value.Value;
                    }
                    catch (RequestFailedException ex)
                    {
                        return $"<error: Unable to retrieve app configuration key '{key}', '{ex.Message}'>";
                    }
                });
            };
        }
    }
}
