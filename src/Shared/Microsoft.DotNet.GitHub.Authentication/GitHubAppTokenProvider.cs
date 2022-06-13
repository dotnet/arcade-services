// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using System.Text;
using Microsoft.Extensions.Internal;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.DotNet.GitHub.Authentication
{
    public class GitHubAppTokenProvider : IGitHubAppTokenProvider
    {
        private readonly ISystemClock _clock;
        private readonly IOptionsMonitor<GitHubTokenProviderOptions> _options;

        public GitHubAppTokenProvider(ISystemClock clock, IOptionsMonitor<GitHubTokenProviderOptions> options = null)
        {
            _clock = clock;
            _options = options;
        }

        public string GetAppToken()
        {
            var options = _options.CurrentValue;
            return GetAppToken(options.GitHubAppId, options.PrivateKey);
        }
        /// <summary>
        /// Get an app token using the <see cref="GitHubTokenProviderOptions"/> corresponding to the specified
        /// <see href="https://docs.microsoft.com/en-us/dotnet/core/extensions/options#named-options-support-using-iconfigurenamedoptions">named option</see>.
        /// </summary>
        public string GetAppToken(string name)
        {
            var options = _options.Get(name);
            return GetAppToken(options.GitHubAppId, options.PrivateKey);
        }

        private string GetAppToken(int gitHubAppId, string privateKey)
        {
            var handler = new JwtSecurityTokenHandler
            {
                SetDefaultTimesOnTokenCreation = false
            };
            using var rsa = RSA.Create(4096); // lgtm [cs/cryptography/default-rsa-key-construction] False positive. This does not use the default constructor.
            rsa.ImportFromPem(privateKey);
            var rsaSecurityKey = new RsaSecurityKey(rsa)
            {
                CryptoProviderFactory =
                {
                    // Since we control the lifetime of the key, they can't cache it, since we are about to dispose it
                    CacheSignatureProviders = false
                }
            };
            var signingCredentials = new SigningCredentials(rsaSecurityKey, SecurityAlgorithms.RsaSha256);
            var dsc = new SecurityTokenDescriptor
            {
                IssuedAt = _clock.UtcNow.AddMinutes(-1).UtcDateTime,
                Expires = _clock.UtcNow.AddMinutes(9).UtcDateTime,
                Issuer = gitHubAppId.ToString(),
                SigningCredentials = signingCredentials
            };
            SecurityToken token = handler.CreateToken(dsc);
            return handler.WriteToken(token);
        }
    }
}
