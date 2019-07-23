// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitHubJwt;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Maestro.GitHub
{
    public class GitHubTokenProvider : IGitHubTokenProvider
    {
        private readonly IOptions<GitHubTokenProviderOptions> _options;
        private readonly ConcurrentDictionary<long, AccessToken> _tokenCache;
        public ILogger<GitHubTokenProvider> _logger;

        public GitHubTokenProvider(IOptions<GitHubTokenProviderOptions> options,
                                   ILogger<GitHubTokenProvider> logger)
        {
            _options = options;
            _logger = logger;
            _tokenCache = new ConcurrentDictionary<long, AccessToken>();
        }

        public GitHubTokenProviderOptions Options => _options.Value;

        public async Task<string> GetTokenForInstallation(long installationId)
        {
            if (TryGetCachedToken(installationId, out string cachedToken))
            {
                return cachedToken;
            }

            return await ExponentialRetry.RetryAsync(
                async () =>
                {
                    string jwt = GetAppToken();
                    var product = new ProductHeaderValue(Options.ApplicationName, Options.ApplicationVersion);
                    var appClient = new Octokit.GitHubClient(product) { Credentials = new Credentials(jwt, AuthenticationType.Bearer) };
                    AccessToken token = await appClient.GitHubApps.CreateInstallationToken(installationId);
                    UpdateTokenCache(installationId, token);
                    return token.Token;
                },
                ex => _logger.LogError(ex, $"Failed to get a github token for installation id {installationId}, retrying"),
                ex => ex is ApiException && ((ApiException)ex).StatusCode == HttpStatusCode.InternalServerError);
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

        private bool TryGetCachedToken(long installationId, out string cachedToken)
        {
            cachedToken = null;

            if (!_tokenCache.ContainsKey(installationId))
            {
                return false;
            }

            AccessToken token = _tokenCache[installationId];

            // If the cached token will expire in less than 30 minutes we won't use it and let GetTokenForInstallation generate a new one
            // and update the cache
            if (DateTimeOffset.Now.Subtract(token.ExpiresAt).TotalMinutes < 30)
            {
                return false;
            }

            cachedToken = token.Token;
            return true;
        }

        private void UpdateTokenCache(long installationId, AccessToken accessToken)
        {
            _tokenCache.AddOrUpdate(installationId, accessToken, (installation, token) => token = accessToken);
        }
    }
}
