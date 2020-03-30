using System;
using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public static class AppConfigurationConfigMapper
    {
        private static readonly Regex ConfigReferenceRegex = new Regex(@"\[config\((?<key>[^])]+)\)\]");

        public static Func<string, string> Create(IConfiguration bootstrapConfiguration)
        {
            string appConfigurationUri = bootstrapConfiguration[ConfigurationConstants.AppConfigurationUriConfigurationKey];
            if (appConfigurationUri == null)
            {
                return value => value;
            }
            TokenCredential credentials = ServiceConfigurationExtensions.GetAzureTokenCredential(bootstrapConfiguration);
            var client = new ConfigurationClient(new Uri(appConfigurationUri), credentials);
            return RegexConfigMapper.Create(ConfigReferenceRegex, key =>
            {
                try
                {
                    const string featureManagementPrefix = "FeatureManagement:";
                    if (key.StartsWith(featureManagementPrefix))
                    {
                        key = key.Substring(featureManagementPrefix.Length);
                        var featureFlagData = JObject.Parse(client.GetConfigurationSetting(".appconfig.featureflag/" + key).Value.Value);
                        if (featureFlagData.Value<bool>("enabled"))
                        {
                            return "true";
                        }

                        return "false";
                    }
                    return client.GetConfigurationSetting(key).Value.Value;
                }
                catch (RequestFailedException)
                {
                    return "";
                }
            });
        }
    }
}
