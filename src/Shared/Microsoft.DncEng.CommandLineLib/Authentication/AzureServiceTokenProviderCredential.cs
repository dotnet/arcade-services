using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Services.AppAuthentication;

namespace Microsoft.DncEng.CommandLineLib.Authentication;

public class AzureServiceTokenProviderCredential : TokenCredential
{
    private readonly AzureServiceTokenProvider _tokenProvider = new AzureServiceTokenProvider();

    private readonly Func<string[], string> _scopesToResource = (Func<string[], string>)
        typeof(DeviceCodeCredential).Assembly
            .GetType("Azure.Identity.ScopeUtilities")!
            .GetMethod("ScopesToResource")!
            .CreateDelegate(typeof(Func<string[], string>));

    private readonly string _tenantId;

    public AzureServiceTokenProviderCredential(string tenantId)
    {
        _tenantId = tenantId;
    }

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        try
        {
            string resource = _scopesToResource(requestContext.Scopes);
            AppAuthenticationResult result = await _tokenProvider.GetAuthenticationResultAsync(resource, _tenantId, cancellationToken);
            return new AccessToken(result.AccessToken, result.ExpiresOn);
        }
        catch (AzureServiceTokenProviderException ex)
        {
            throw new CredentialUnavailableException("AzureServiceTokenProviderCredential unable to authenticate", ex);
        }
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return Task.Run(async () => await GetTokenAsync(requestContext, cancellationToken), cancellationToken).GetAwaiter().GetResult();
    }
}
