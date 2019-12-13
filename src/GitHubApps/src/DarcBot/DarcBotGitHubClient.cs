// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using Octokit;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DarcBot
{
    internal class DarcBotGitHubClient : IGitHubClient
    {
        public IConnection Connection => _gitHubClient.Connection;

        public IAuthorizationsClient Authorization => _gitHubClient.Authorization;

        public IActivitiesClient Activity => _gitHubClient.Activity;

        public IGitHubAppsClient GitHubApps => _gitHubClient.GitHubApps;

        public IIssuesClient Issue => _gitHubClient.Issue;

        public IMigrationClient Migration => _gitHubClient.Migration;

        public IMiscellaneousClient Miscellaneous => _gitHubClient.Miscellaneous;

        public IOauthClient Oauth => _gitHubClient.Oauth;

        public IOrganizationsClient Organization => _gitHubClient.Organization;

        public IPullRequestsClient PullRequest => _gitHubClient.PullRequest;

        public IRepositoriesClient Repository => _gitHubClient.Repository;

        public IGistsClient Gist => _gitHubClient.Gist;

        public IUsersClient User => _gitHubClient.User;

        public IGitDatabaseClient Git => _gitHubClient.Git;

        public ISearchClient Search => _gitHubClient.Search;

        public IEnterpriseClient Enterprise => _gitHubClient.Enterprise;

        public IReactionsClient Reaction => _gitHubClient.Reaction;

        public IChecksClient Check => _gitHubClient.Check;

        private IGitHubClient _gitHubClient;

        private ILogger _log;

        public DarcBotGitHubClient(int installationId, ILogger log = null)
        {
            _log = log;

            string installationToken = GetTokenForInstallationAsync(installationId).Result;
            var userAgent = new Octokit.ProductHeaderValue("DarcBot");
            _gitHubClient = new GitHubClient(userAgent)
            {
                Credentials = new Credentials(installationToken),
            };
            return;
        }

        private string GetTokenForApplication()
        {
            LogInformation("Entering GetTokenForApplication");
            string pemKey = System.Environment.GetEnvironmentVariable("PemKey");
            RSAParameters rsaParameters = CryptoHelper.GetRsaParameters(pemKey);
            var key = new RsaSecurityKey(rsaParameters);
            var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
            var now = DateTime.UtcNow;
            int timeoutInMinutes = int.Parse(System.Environment.GetEnvironmentVariable("TokenExpirationInMinutes"));
            var token = new JwtSecurityToken(
                claims: new[]
                {
                    new Claim("iat", ToUnixTimeStamp(now).ToString(), ClaimValueTypes.Integer),
                    new Claim("exp", ToUnixTimeStamp(now.AddMinutes(timeoutInMinutes)).ToString(), ClaimValueTypes.Integer),
                    new Claim("iss", System.Environment.GetEnvironmentVariable("AppId"))
                },
                signingCredentials: creds);
            var jwt = new JwtSecurityTokenHandler().WriteToken(token);
            LogInformation("Exiting GetTokenForApplication");
            return jwt;
        }
        private async Task<string> GetTokenForInstallationAsync(int installationId)
        {
            LogInformation("Entering GetTokenForInstallationAsync");
            var appToken = GetTokenForApplication();
            using (var client = new HttpClient())
            {
                string url = $"https://api.github.com/installations/{installationId}/access_tokens";
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Headers =
                    {
                        Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", appToken),
                        UserAgent =
                        {
                            ProductInfoHeaderValue.Parse("DarcBot"),
                        },
                        Accept =
                        {
                            MediaTypeWithQualityHeaderValue.Parse("application/vnd.github.machine-man-preview+json")
                        }
                    }
                };
                using (var response = await client.SendAsync(request))
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var obj = JObject.Parse(json);
                    LogInformation($"GetTokenForInstallation json response: {json}");
                    LogInformation("Exiting GetTokenForInstallationAsync");
                    return obj["token"]?.Value<string>();
                }
            }
        }

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static int ToUnixTimeStamp(DateTime date)
        {
            return (int)(date - UnixEpoch).TotalSeconds;
        }

        public void SetRequestTimeout(TimeSpan timeout)
        {
            _gitHubClient.SetRequestTimeout(timeout);
        }

        public ApiInfo GetLastApiInfo()
        {
            return _gitHubClient.GetLastApiInfo();
        }

        private void LogInformation(string text)
        {
            if(_log != null)
            {
                _log.LogInformation(text);
            }
        }
    }
}
