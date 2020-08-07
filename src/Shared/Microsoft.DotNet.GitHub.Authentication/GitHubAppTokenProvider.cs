// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitHubJwt;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Options;
using System.Text;

namespace Microsoft.DotNet.GitHub.Authentication
{
    public class GitHubAppTokenProvider : IGitHubAppTokenProvider
    {
        private readonly IOptions<GitHubTokenProviderOptions> _options;

        public GitHubAppTokenProvider(IOptions<GitHubTokenProviderOptions> options = null)
        {
            _options = options;
        }

        public string GetAppToken()
        {
            return GetAppToken(_options.Value.GitHubAppId, new StringPrivateKeySource(_options.Value.PrivateKey));
        }

        public string GetAppTokenFromEnvironmentVariableBase64(int gitHubAppId, string environmentVariableName)
        {
            string encodedKey = System.Environment.GetEnvironmentVariable(environmentVariableName);
            byte[] keydata = System.Convert.FromBase64String(encodedKey);
            string privateKey = Encoding.UTF8.GetString(keydata);

            return GetAppToken(gitHubAppId, new StringPrivateKeySource(privateKey));
        }

        private string GetAppToken(int gitHubAppId, IPrivateKeySource privateKeySource)
        {
            var generator = new GitHubJwtFactory(
                privateKeySource,
                new GitHubJwtFactoryOptions
                {
                    AppIntegrationId = gitHubAppId,
                    // Due to clock drift, use 9:30 to avoid "'Expiration time' claim ('exp') is too far in the future"
                    ExpirationSeconds = 570 
                });
            return generator.CreateEncodedJwtToken();
        }
    }
}
