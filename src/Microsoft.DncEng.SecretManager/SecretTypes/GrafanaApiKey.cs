using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("grafana-api-key")]
public class GrafanaApiKey : GenericAccessToken
{
    private readonly string[] _expirationDateFormats = new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss" };

    protected override string HelpMessage => "Please login to https://{0}/org/apikeys using your GitHub account and create a new API key.";
    protected override string TokenName => "Grafana API key";
    protected override string TokenFormatDescription => "base64 encoded json";
    protected override string ExpirationFormatDescription => "format yyyy-MM-dd followed by optional time part hh:mm:ss or empty for no expiration";
    protected override bool HasExpiration => true;

    protected override IEnumerable<KeyValuePair<string, string>> EnvironmentToHost => new[]
    {
        new KeyValuePair<string, string>( "production", "dotnet-eng-grafana.westus2.cloudapp.azure.com" ),
        new KeyValuePair<string, string>( "staging", "dotnet-eng-grafana-staging.westus2.cloudapp.azure.com" )
    };

    public GrafanaApiKey(ISystemClock clock, IConsole console) : base(clock, console)
    {
    }

    protected override bool TryParseExpirationDate(string value, out DateTime parsedValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsedValue = DateTime.MaxValue;
            return true;
        }
        return DateTime.TryParseExact(value, _expirationDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValue);
    }

    protected override bool ValidateToken(string token)
    {
        try
        {
            string jsonString = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            JsonDocument json = JsonDocument.Parse(jsonString, new JsonDocumentOptions());
            if (json.RootElement.TryGetProperty("n", out JsonElement keyName))
                Console.WriteLine($"API key was entered with name {keyName}.");

            return true;
        }
        catch
        {
            return false;
        }
    }
}
