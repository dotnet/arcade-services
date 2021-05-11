using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("grafana-api-key")]
    public class GrafanaApiKey : SecretType
    {
        private readonly TimeSpan _rotateBeforeExpiration = TimeSpan.FromDays(-15);
        private readonly string[] _expirationDateFormats = new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss" };
        private readonly ISystemClock _clock;
        private readonly IConsole _console;

        public GrafanaApiKey(ISystemClock clock, IConsole console)
        {
            _clock = clock;
            _console = console;
        }

        public override async Task<List<SecretData>> RotateValues(IReadOnlyDictionary<string, string> parameters, RotationContext context, CancellationToken cancellationToken)
        {
            if (!_console.IsInteractive)
            {
                throw new InvalidOperationException($"User intervention required for creation or rotation of a Grafana API key.");
            }

            var pat = await _console.PromptAndValidateAsync("API key",
                "API key must be base64 encoded json.",
                ValidateAPIKey);

            var expiration = await _console.PromptAndValidateAsync("API key expiration (yyyy-MM-dd)",
                "API key expiration must be in the format yyyy-MM-dd followed by optional time part hh:mm:ss.",
                (string value, out DateTime parsedValue) =>
                    DateTime.TryParseExact(value, _expirationDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValue));

            return new List<SecretData>() { new SecretData(pat, expiration, expiration.Add(_rotateBeforeExpiration)) };
        }

        private bool ValidateAPIKey(string value)
        {
            try
            {
                string jsonString = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                JsonDocument json = JsonDocument.Parse(jsonString, new JsonDocumentOptions());
                if(json.RootElement.TryGetProperty("n", out JsonElement keyName))
                    _console.WriteLine($"API key was entered with name {keyName}.");

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
