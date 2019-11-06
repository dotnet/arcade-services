// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;
using GitHubJwt;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace Microsoft.Dotnet.GitHub.Authentication
{
    public class GitHubTokenProvider : IGitHubTokenProvider
    {
        private readonly IInstallationLookup _installationLookup;
        private readonly IOptions<GitHubTokenProviderOptions> _options;
        private readonly ConcurrentDictionary<long, AccessToken> _tokenCache;
        public readonly ILogger<GitHubTokenProvider> _logger;

        public GitHubTokenProvider(
            IInstallationLookup installationLookup,
            IOptions<GitHubTokenProviderOptions> options,
            ILogger<GitHubTokenProvider> logger)
        {
            _installationLookup = installationLookup;
            _options = options;
            _logger = logger;
            _tokenCache = new ConcurrentDictionary<long, AccessToken>();
        }

        public GitHubTokenProviderOptions Options => _options.Value;

        public async Task<string> GetTokenForInstallationAsync(long installationId)
        {
            if (TryGetCachedToken(installationId, out AccessToken cachedToken))
            {
                _logger.LogInformation($"Cached token obtained for GitHub installation {installationId}. Expires at {cachedToken.ExpiresAt}.");
                return cachedToken.Token;
            }

            return await ExponentialRetry.RetryAsync(
                async () =>
                {
                    string jwt = GetAppToken();
                    var product = new ProductHeaderValue(Options.ApplicationName, Options.ApplicationVersion);
                    var appClient = new Octokit.GitHubClient(product) { Credentials = new Credentials(jwt, AuthenticationType.Bearer) };
                    AccessToken token = await appClient.GitHubApps.CreateInstallationToken(installationId);
                    _logger.LogInformation($"New token obtained for GitHub installation {installationId}. Expires at {token.ExpiresAt}.");
                    UpdateTokenCache(installationId, token);
                    return token.Token;
                },
                ex => _logger.LogError(ex, $"Failed to get a github token for installation id {installationId}, retrying"),
                ex => ex is ApiException && ((ApiException)ex).StatusCode == HttpStatusCode.InternalServerError);
        }

        public async Task<string> GetTokenForRepository(string repositoryUrl)
        {
            return await GetTokenForInstallationAsync(await _installationLookup.GetInstallationId(repositoryUrl));
        }

        private string GetAppToken()
        {
            var generator = new GitHubJwtFactory(
                new StringPrivateKeySource(Options.PrivateKey),
                new GitHubJwtFactoryOptions
                {
                    AppIntegrationId = Options.GitHubAppId,
                    ExpirationSeconds = 600
                });
            return generator.CreateEncodedJwtToken();
        }

        private bool TryGetCachedToken(long installationId, out AccessToken cachedToken)
        {
            cachedToken = null;

            if (!_tokenCache.ContainsKey(installationId))
            {
                return false;
            }

            AccessToken token = _tokenCache[installationId];

            // If the cached token will expire in less than 30 minutes we won't use it and let GetTokenForInstallationAsync generate a new one
            // and update the cache
            if (token.ExpiresAt.Subtract(DateTimeOffset.Now).TotalMinutes < 30)
            {
                return false;
            }

            cachedToken = token;
            return true;
        }

        private void UpdateTokenCache(long installationId, AccessToken accessToken)
        {
            _tokenCache.AddOrUpdate(installationId, accessToken, (installation, token) => token = accessToken);
        }
    }
}
