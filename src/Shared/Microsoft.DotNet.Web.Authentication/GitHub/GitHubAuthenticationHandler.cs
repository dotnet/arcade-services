// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Web.Authentication.GitHub
{
    public class GitHubAuthenticationHandler : OAuthHandler<GitHubAuthenticationOptions>
    {
        private readonly GitHubClaimResolver _claimResolver;

        public GitHubAuthenticationHandler(
            GitHubClaimResolver claimResolver,
            IOptionsMonitor<GitHubAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock) : base(options, logger, encoder, clock)
        {
            _claimResolver = claimResolver;
        }

        protected override async Task<AuthenticationTicket> CreateTicketAsync(
            ClaimsIdentity identity,
            AuthenticationProperties properties,
            OAuthTokenResponse tokens)
        {
            string accessToken = tokens.AccessToken;
            (IEnumerable<Claim> claims, JObject user) = await _claimResolver.GetUserInformation(accessToken, Context.RequestAborted);
            identity.AddClaims(claims);
            JsonElement rootElement;
            using (JsonDocument jsonDocument = JsonDocument.Parse(user.ToString()))
            {
                rootElement = jsonDocument.RootElement.Clone();
            }

            var context = new OAuthCreatingTicketContext(
                new ClaimsPrincipal(identity),
                properties,
                Context,
                Scheme,
                Options,
                Backchannel,
                tokens,
                rootElement);
            await Options.Events.CreatingTicket(context);
            return new AuthenticationTicket(context.Principal, context.Properties, context.Scheme.Name);
        }
    }
}
