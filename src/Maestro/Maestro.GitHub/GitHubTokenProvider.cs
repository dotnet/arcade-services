// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitHubJwt;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using System.Net;
using System.Threading.Tasks;

namespace Maestro.GitHub
{
    public class GitHubTokenProvider : IGitHubTokenProvider
    {
        private readonly IOptions<GitHubTokenProviderOptions> _options;
        public ILogger<GitHubTokenProvider> _logger;

        public GitHubTokenProvider(IOptions<GitHubTokenProviderOptions> options,
                                   ILogger<GitHubTokenProvider> logger)
        {
            _options = options;
            _logger = logger;
        }

        public GitHubTokenProviderOptions Options => _options.Value;

        public async Task<string> GetTokenForInstallation(long installationId)
        {
            return await ExponentialRetry.RetryAsync(
                async () =>
                {
                    string jwt = GetAppToken();
                    var product = new ProductHeaderValue(Options.ApplicationName, Options.ApplicationVersion);
                    var appClient = new Octokit.GitHubClient(product) { Credentials = new Credentials(jwt, AuthenticationType.Bearer) };
                    AccessToken token = await appClient.GitHubApps.CreateInstallationToken(installationId);
                    _logger.LogInformation($"New token obtained for GitHub installation {installationId}. Expires at {token.ExpiresAt}.");
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
    }
}
