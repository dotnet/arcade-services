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
        
        public string GetAppToken(string name)
        {
            var options = _options.Get(name);
            return GetAppToken(options.GitHubAppId, options.PrivateKey);
        }

        private string GetAppToken(int gitHubAppId, string privateKey)
        {
            var handler = new JwtSecurityTokenHandler();
            byte[] key = Encoding.ASCII.GetBytes(privateKey);
            using var rsa = RSA.Create();
            var dsc = new SecurityTokenDescriptor
            {
                IssuedAt = _clock.UtcNow.AddMinutes(-1).UtcDateTime,
                Expires = _clock.UtcNow.AddMinutes(9).UtcDateTime,
                Issuer = gitHubAppId.ToString(),
                SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
            };
            SecurityToken token = handler.CreateToken(dsc);
            return handler.WriteToken(token);
        }
    }
}
