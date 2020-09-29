using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.ApplicationInsights;
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

            TelemetryClient telemetry = ConfigMapper.GetTelemetryClient(bootstrapConfiguration);
            TokenCredential credentials = ServiceConfigurationExtensions.GetAzureTokenCredential(bootstrapConfiguration);
            var client = new ConfigurationClient(new Uri(appConfigurationUri), credentials);

            void TrackEvent(string name, string key, Exception? error)
            {
                telemetry.TrackEvent(name, new Dictionary<string, string>
                {
                    ["name"] = key,
                    ["appConfigurationUri"] = appConfigurationUri,
                    ["success"] = error == null ? "true" : "false",
                });
                if (error != null)
                {
                    telemetry.TrackException(error);
                }
            }

            return RegexConfigMapper.Create(ConfigReferenceRegex, key =>
            {
                const string featureManagementPrefix = "FeatureManagement:";
                if (key.StartsWith(featureManagementPrefix))
                {
                    key = key.Substring(featureManagementPrefix.Length);
                    var (result, error) = GetFeatureFlagValue(client, key);
                    TrackEvent("AppConfigurationFeatureFlagAccess", key, error);
                    return result;
                }
                else
                {
                    var (result, error) = GetConfigurationSetting(client, key);
                    TrackEvent("AppConfigurationSettingAccess", key, error);
                    return result;
                }
            });
        }

        private static (string, Exception?) GetConfigurationSetting(ConfigurationClient client, string key)
        {
            string result = "";
            Exception? error = null;
            try
            {
                result = client.GetConfigurationSetting(key).Value.Value;
            }
            catch (RequestFailedException ex)
            {
                error = ex;
            }

            return (result, error);
        }

        private static (string, Exception?) GetFeatureFlagValue(ConfigurationClient client, string key)
        {
            string result = "false";
            Exception? error = null;
            try
            {
                var featureFlagData =
                    JObject.Parse(client.GetConfigurationSetting(".appconfig.featureflag/" + key).Value.Value);
                if (featureFlagData.Value<bool>("enabled"))
                {
                    result = "true";
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }

            return (result, error);
        }
    }
}
