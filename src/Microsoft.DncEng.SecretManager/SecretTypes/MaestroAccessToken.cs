using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("maestro-access-token")]
public class MaestroAccessToken : GenericAccessToken
{
    protected override string HelpMessage => "Please login to https://{0}/Account/Tokens using your GitHub account and create a new token.";
    protected override string TokenName => "Maestro Access Token";
    protected override string TokenFormatDescription => "base64 url encoded with at least 24 characters";
    protected override bool HasExpiration => false;

    protected override IEnumerable<KeyValuePair<string, string>> EnvironmentToHost => new[]
    {
        new KeyValuePair<string, string>( "production", "maestro-prod.westus2.cloudapp.azure.com" ),
        new KeyValuePair<string, string>( "staging", "maestro-int.westus2.cloudapp.azure.com" )
    };

    public MaestroAccessToken(ISystemClock clock, IConsole console) : base(clock, console)
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
