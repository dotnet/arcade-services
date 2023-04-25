using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace Microsoft.DncEng.CommandLineLib.Authentication;

public class TokenCredentialProvider
{
    public static string MsftAdTenantId => "72f988bf-86f1-41af-91ab-2d7cd011db47";
    private readonly IConsole _console;
    private readonly InteractiveTokenCredentialProvider _interactiveTokenCredentialProvider;

    public TokenCredentialProvider(IConsole console, InteractiveTokenCredentialProvider interactiveTokenCredentialProvider)
    {
        _console = console;
        _interactiveTokenCredentialProvider = interactiveTokenCredentialProvider;
    }

    public async Task<TokenCredential> GetCredentialAsync()
    {
        var creds = new List<TokenCredential>
        {
            new AzureServiceTokenProviderCredential(MsftAdTenantId),
        };
        if (_console.IsInteractive)
        {
            creds.Add(await _interactiveTokenCredentialProvider.GetCredentialAsync());
        }

        return new ChainedTokenCredential(creds.ToArray());
    }
}
