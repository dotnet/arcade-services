// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Maestro.Web
{
    public class GitHubAuthenticationHandler : OAuthHandler<GitHubAuthenticationOptions>
    {
        public GitHubAuthenticationHandler(
            IOptionsMonitor<GitHubAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock) : base(options, logger, encoder, clock)
        {
        }

        protected override async Task<AuthenticationTicket> CreateTicketAsync(
            ClaimsIdentity identity,
            AuthenticationProperties properties,
            OAuthTokenResponse tokens)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, Options.UserInformationEndpoint)
            {
                Headers =
                {
                    Accept = {new MediaTypeWithQualityHeaderValue("application/json")},
                    Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken)
                }
            })
            using (HttpResponseMessage response = await Backchannel.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                Context.RequestAborted))
            {
                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    if (body.Length > 1024)
                    {
                        body = body.Substring(0, 1024);
                    }

                    Logger.LogError(
                        "An error occurred while retrieving the user profile: the remote server returned a {Status} response with the following payload: {Headers} {Body}.",
                        response.StatusCode,
                        response.Headers.ToString(),
                        body);
                    throw new HttpRequestException("An error occurred while retrieving the user profile.");
                }

                JObject payload = JObject.Parse(await response.Content.ReadAsStringAsync());
                void AddClaim(string type, string value)
                {
                    if (string.IsNullOrEmpty(value))
                        return;
                    identity.AddClaim(
                        new Claim(
                            type,
                            value,
                            ClaimValueTypes.String,
                            Options.ClaimsIssuer));
                }

                AddClaim(ClaimTypes.NameIdentifier, payload.Value<string>("id"));
                AddClaim(ClaimTypes.Name, payload.Value<string>("login"));
                AddClaim(ClaimTypes.Email, payload.Value<string>("email"));
                AddClaim("urn:github:name", payload.Value<string>("name"));
                AddClaim("urn:github:url", payload.Value<string>("url"));

                var context = new OAuthCreatingTicketContext(
                    new ClaimsPrincipal(identity),
                    properties,
                    Context,
                    Scheme,
                    Options,
                    Backchannel,
                    tokens,
                    payload);
                await Options.Events.CreatingTicket(context);
                return new AuthenticationTicket(context.Principal, context.Properties, context.Scheme.Name);
            }
        }
    }
}
