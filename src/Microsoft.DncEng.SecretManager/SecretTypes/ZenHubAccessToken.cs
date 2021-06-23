using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("zenhub-access-token")]
    public class ZenHubAccessToken : GenericAccessToken
    {
        private readonly string[] _expirationDateFormats = new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss" };

        protected override string HelpMessage => "Please login to https://app.zenhub.com/dashboard/tokens using your GitHub account and create a new token.";
        protected override string TokenName => "ZenHub API token";
        protected override string TokenFormatDescription => "hex string";
        protected override string ExpirationFormatDescription => "format yyyy-MM-dd followed by optional time part hh:mm:ss or empty for no expiration";
        protected override bool HasExpiration => false;

        protected override IEnumerable<KeyValuePair<string, string>> EnvironmentToHost =>
            Array.Empty<KeyValuePair<string, string>>();

        public ZenHubAccessToken(ISystemClock clock, IConsole console) : base(clock, console)
        {
        }

        protected override bool TryParseExpirationDate(string value, out DateTime parsedValue)
        {
            throw new NotSupportedException();
        }

        protected override bool ValidateToken(string token)
        {
            return Regex.IsMatch(token, "^[0-9a-fA-F]+$");
        }
    }
}
