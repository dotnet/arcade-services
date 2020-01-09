// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitHubJwt;
using Microsoft.Extensions.Options;

namespace Microsoft.Dotnet.GitHub.Authentication
{
    public class GitHubAppTokenProvider
    {
        private readonly IOptions<GitHubTokenProviderOptions> _options;

        public GitHubAppTokenProvider(IOptions<GitHubTokenProviderOptions> options)
        {
            _options = options;
        }

        public string GetAppToken()
        {
            var generator = new GitHubJwtFactory(
                new StringPrivateKeySource(_options.Value.PrivateKey),
                new GitHubJwtFactoryOptions
                {
                    AppIntegrationId = _options.Value.GitHubAppId,
                    ExpirationSeconds = 600
                });
            return generator.CreateEncodedJwtToken();
        }
    }
}
