// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using Octokit.Internal;

namespace Microsoft.DotNet.Web.Authentication.GitHub
{
    public class GitHubClaimResolver
    {
        private const string AccessTokenClaim = "urn:github:access_token";
        private readonly ILogger<GitHubClaimResolver> _logger;
        private readonly IMemoryCache _cache;
        private readonly IOptionsMonitor<GitHubAuthenticationOptions> _options;
        private readonly IGitHubClientFactory _gitHubClientFactory;

        public GitHubClaimResolver(
            IMemoryCache cache,
            IOptionsMonitor<GitHubAuthenticationOptions> options,
            ILoggerFactory logger,
            IGitHubClientFactory gitHubClientFactory)
        {
            _cache = cache;
            _options = options;
            _gitHubClientFactory = gitHubClientFactory;
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
                IGitHubClient client = _gitHubClientFactory.CreateGitHubClient(accessToken);
                User user = await client.User.Current();

                _logger.LogInformation("Successfully fetched user data");

                ImmutableArray<Claim>.Builder claims = ImmutableArray.CreateBuilder<Claim>();

                void AddClaim(string type, string value)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        claims.Add(new Claim(type, value, ClaimValueTypes.String, options.ClaimsIssuer));
                    }
                }

                AddClaim(ClaimTypes.NameIdentifier, user.Id.ToString());
                AddClaim(ClaimTypes.Name, user.Login);
                AddClaim(ClaimTypes.Email, user.Email);
                AddClaim("urn:github:name", user.Name);
                AddClaim("urn:github:url", user.Url);
                AddClaim(AccessTokenClaim, accessToken);

                var userInformation = new UserInformation(claims.ToImmutable(), user);
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
                IGitHubClient client = _gitHubClientFactory.CreateGitHubClient(accessToken);

                ImmutableArray<Claim>.Builder claims = ImmutableArray.CreateBuilder<Claim>();

                void AddClaim(string type, string value)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        claims.Add(new Claim(type, value, ClaimValueTypes.String, options.ClaimsIssuer));
                    }
                }

                {
                    IReadOnlyList<Organization> organizations = await client.Organization.GetAllForCurrent();
                    _logger.LogInformation("Fetched {orgCount} orgs", organizations.Count);
                    foreach (Organization org in organizations)
                    {
                        string orgLogin = org.Login?.ToLowerInvariant();
                        AddClaim(ClaimTypes.Role, GetOrganizationRole(orgLogin));
                        AddClaim("urn:github:org", orgLogin);
                    }
                }

                {
                    IReadOnlyList<Team> teams = await client.Organization.Team.GetAllForCurrent();
                    _logger.LogInformation("Fetched {teamCount} teams", teams.Count);
                    foreach (Team team in teams)
                    {
                        string teamName = team.Name?.ToLowerInvariant();
                        string orgName = team.Organization.Login?.ToLowerInvariant();
                        string fullName = orgName + ":" + teamName;
                        AddClaim(ClaimTypes.Role, GetTeamRole(orgName, teamName));
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
        
        public static string GetOrganizationRole(string organizationLogin)
        {
            return $"github:org:{organizationLogin.ToLowerInvariant()}";

        }

        public static string GetTeamRole(string organizationLogin, string teamName)
        {
            return $"github:team:{organizationLogin.ToLowerInvariant()}:{teamName.ToLowerInvariant()}";
        }

        public struct UserInformation
        {
            public UserInformation(IEnumerable<Claim> claims, User user)
            {
                Claims = claims;
                User = user;
            }

            public IEnumerable<Claim> Claims { get; }
            public User User { get; }

            public void Deconstruct(out IEnumerable<Claim> claims, out User user)
            {
                claims = Claims;
                user = User;
            }
        }
    }
}
