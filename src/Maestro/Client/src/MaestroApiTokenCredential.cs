
using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace Microsoft.DotNet.Maestro.Client
{
    public class MaestroApiTokenCredential : TokenCredential
    {
        public MaestroApiTokenCredential(string token)
        {
            Token = token;
        }

        public string Token { get; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new AccessToken(Token, DateTimeOffset.MaxValue);
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new ValueTask<AccessToken>(new AccessToken(Token, DateTimeOffset.MaxValue));
        }
    }
}
