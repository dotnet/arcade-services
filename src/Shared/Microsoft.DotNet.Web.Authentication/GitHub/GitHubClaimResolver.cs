// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Web.Authentication.GitHub
{
    public class GitHubClaimResolver
    {
        private const string AccessTokenClaim = "urn:github:access_token";
        private readonly ILogger<GitHubClaimResolver> _logger;
        private readonly IMemoryCache _cache;
        private readonly IOptionsMonitor<GitHubAuthenticationOptions> _options;

        public GitHubClaimResolver(
            IMemoryCache cache,
            IOptionsMonitor<GitHubAuthenticationOptions> options,
            ILoggerFactory logger)
        {
            _cache = cache;
            _options = options;
            _logger = logger.CreateLogger<GitHubClaimResolver>();
        }

        private struct UserInfoKey
        {
            public UserInfoKey(string accessToken)
            {
                AccessToken = accessToken;
            }

            public string AccessToken { get; }

            public bool Equals(UserInfoKey other)
            {
                return AccessToken == other.AccessToken;
            }

            public override bool Equals(object obj)
            {
                return obj is UserInfoKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (AccessToken != null ? AccessToken.GetHashCode() : 0);
            }
        }

        private struct GroupInfoKey
        {
            public GroupInfoKey(string accessToken)
            {
                AccessToken = accessToken;
            }

            public string AccessToken { get; }

            public bool Equals(GroupInfoKey other)
            {
                return AccessToken == other.AccessToken;
            }

            public override bool Equals(object obj)
            {
                return obj is GroupInfoKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (AccessToken != null ? AccessToken.GetHashCode() : 0);
            }
        }

        public async Task<IEnumerable<Claim>> GetUserInformationClaims(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            return (await GetUserInformation(accessToken, cancellationToken)).Claims;
        }

        public async Task<UserInformation> GetUserInformation(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Getting user information");
            return await _cache.GetOrCreateAsync(new UserInfoKey(accessToken),
                cacheEntry =>
                {
                    cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                    return Execute();
                });

            async Task<UserInformation> Execute()
            {
                _logger.LogInformation("Fetching fresh user info...");
                GitHubAuthenticationOptions options = _options.CurrentValue;

                JObject payload = await GetResponseJsonPayloadAsync(options.UserInformationEndpoint,
                    accessToken,
                    options,
                    async r => JObject.Parse(await r.Content.ReadAsStringAsync()),
                    cancellationToken);

                _logger.LogInformation("Successfully fetched user data");

                ImmutableArray<Claim>.Builder claims = ImmutableArray.CreateBuilder<Claim>();

                void AddClaim(string type, string value)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        claims.Add(new Claim(type, value, ClaimValueTypes.String, options.ClaimsIssuer));
                    }
                }

                AddClaim(ClaimTypes.NameIdentifier, payload.Value<string>("id"));
                AddClaim(ClaimTypes.Name, payload.Value<string>("login"));
                AddClaim(ClaimTypes.Email, payload.Value<string>("email"));
                AddClaim("urn:github:name", payload.Value<string>("name"));
                AddClaim("urn:github:url", payload.Value<string>("url"));
                AddClaim(AccessTokenClaim, accessToken);

                var userInformation = new UserInformation(claims.ToImmutable(), payload);
                return userInformation;
            }
        }

        public async Task<IEnumerable<Claim>> GetMembershipClaims(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Getting user membership information...");
            return await _cache.GetOrCreateAsync(new GroupInfoKey(accessToken), 
                cacheEntry =>
                {
                    cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                    return Execute();
                });

            async Task<ImmutableArray<Claim>> Execute()
            {
                _logger.LogInformation("Fetching fresh membership info...");
                GitHubAuthenticationOptions options = _options.CurrentValue;

                ImmutableArray<Claim>.Builder claims = ImmutableArray.CreateBuilder<Claim>();

                void AddClaim(string type, string value)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        claims.Add(new Claim(type, value, ClaimValueTypes.String, options.ClaimsIssuer));
                    }
                }

                {
                    JArray orgPayload = await GetResponseJsonPayloadAsync("https://api.github.com/user/orgs",
                        accessToken,
                        options,
                        async r => JArray.Parse(await r.Content.ReadAsStringAsync()),
                        cancellationToken);

                    _logger.LogInformation("Fetched {orgCount} orgs", orgPayload.Count);

                    foreach (JToken org in orgPayload)
                    {
                        string orgLogin = org.Value<string>("login")?.ToLowerInvariant();
                        AddClaim(ClaimTypes.Role, "github:org:" + orgLogin);
                        AddClaim("urn:github:org", orgLogin);
                    }
                }

                {
                    JArray teamPayload = await GetResponseJsonPayloadAsync("https://api.github.com/user/teams",
                        accessToken,
                        options,
                        async r => JArray.Parse(await r.Content.ReadAsStringAsync()),
                        cancellationToken);

                    _logger.LogInformation("Fetched {teamCount} teams", teamPayload.Count);

                    foreach (JToken team in teamPayload)
                    {
                        string teamName = team.Value<string>("name")?.ToLowerInvariant();
                        string orgName = team["organization"]?.Value<string>("login")?.ToLowerInvariant();
                        string fullName = orgName + "/" + teamName;
                        AddClaim(ClaimTypes.Role, "github:team:" + fullName);
                        AddClaim("urn:github:team", fullName);
                    }
                }

                return claims.ToImmutable();
            }
        }

        public string GetAccessToken(ClaimsPrincipal principal)
        {
            return principal.FindFirst(AccessTokenClaim)?.Value;
        }

        private async Task<T> GetResponseJsonPayloadAsync<T>(
            string url,
            string accessToken,
            GitHubAuthenticationOptions options,
            Func<HttpResponseMessage, Task<T>> parseResponse,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers =
                {
                    Accept = {new MediaTypeWithQualityHeaderValue("application/json")},
                    Authorization = new AuthenticationHeaderValue("Bearer", accessToken)
                }
            })
            {
                using (HttpResponseMessage response = await options.Backchannel.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        return await parseResponse(response);
                    }

                    string body = await response.Content.ReadAsStringAsync();
                    if (body.Length > 1024)
                    {
                        body = body.Substring(0, 1024);
                    }

                    _logger.LogError(
                        "An error occurred while retrieving the user profile: the remote server returned a {Status} response with the following payload: {Headers} {Body}.",
                        response.StatusCode,
                        response.Headers.ToString(),
                        body);
                    throw new HttpRequestException("An error occurred while retrieving the user org membership.");
                }
            }
        }

        public struct UserInformation
        {
            public UserInformation(IEnumerable<Claim> claims, JObject userObject)
            {
                Claims = claims;
                UserObject = userObject;
            }

            public IEnumerable<Claim> Claims { get; }
            public JObject UserObject { get; }

            public void Deconstruct(out IEnumerable<Claim> claims, out JObject user)
            {
                claims = Claims;
                user = UserObject;
            }
        }
    }
}
