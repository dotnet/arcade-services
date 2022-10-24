using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("helix-access-token")]
public class HelixAccessToken : GenericAccessToken
{
    protected override string HelpMessage => "Please login to https://{0}/Account/Tokens using your GitHub account and create a new token.";
    protected override string TokenName => "Helix Access Token";
    protected override string TokenFormatDescription => "base64 url encoded with at least 24 characters";
    protected override bool HasExpiration => false;

    protected override IEnumerable<KeyValuePair<string, string>> EnvironmentToHost => new[]
    {
        new KeyValuePair<string, string>( "production", "helix.dot.net" ),
        new KeyValuePair<string, string>( "staging", "helix.int-dot.net" )
    };

    protected override string ExpirationFormatDescription => throw new NotImplementedException();

    public HelixAccessToken(ISystemClock clock, IConsole console) : base(clock, console)
    {
    }

    protected override bool ValidateToken(string token)
    {
        return Regex.IsMatch(token, "^[a-zA-Z0-9_\\-]{24,}$");
    }

    protected override bool TryParseExpirationDate(string value, out DateTime parsedValue)
    {
        throw new NotImplementedException();
    }
}
