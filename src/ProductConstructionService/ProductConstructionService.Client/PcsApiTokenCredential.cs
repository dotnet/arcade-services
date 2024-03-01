using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace ProductConstructionService.Client
{
    public class PcsApiTokenCredential : TokenCredential
    {
        public PcsApiTokenCredential(string token)
        {
            Token = token;
        }

        public string Token { get; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new AccessToken(Token, DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new ValueTask<AccessToken>(new AccessToken(Token, DateTimeOffset.MaxValue));
    }
}
