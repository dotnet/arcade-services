// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.DotNet.Web.Authentication.GitHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace DotNet.Status.Web
{
    public class GitHubUserTokenHandler : AuthenticationHandler<UserTokenOptions>
    {
        private readonly GitHubClaimResolver _resolver;
        private readonly IDataProtector _dataProtector;
        private readonly ITokenRevocationProvider _revocation;
        private readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Create();
        private readonly ArrayPool<char> _charPool = ArrayPool<char>.Create();

        public GitHubUserTokenHandler(
            GitHubClaimResolver resolver,
            IOptionsMonitor<UserTokenOptions> options,
            IDataProtectionProvider dataProtector,
            ITokenRevocationProvider revocation,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock) : base(options, logger, encoder, clock)
        {
            _resolver = resolver;
            _dataProtector = dataProtector.CreateProtector("github-token");
            _revocation = revocation;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string protectedToken = null;
            if (Request.Headers.TryGetValue("Authorization", out StringValues authHeader) &&
                AuthenticationHeaderValue.TryParse(authHeader.ToString(), out AuthenticationHeaderValue auth) &&
                !string.IsNullOrEmpty(auth.Parameter) &&
                auth.Parameter.Length < 1000)
            {
                switch (auth.Scheme.ToLowerInvariant())
                {
                    case "basic":
                        protectedToken = ParseBasicAuth(auth.Parameter);
                        break;
                    case "bearer":
                        protectedToken = auth.Parameter;
                        break;
                }
            }

            if (string.IsNullOrEmpty(protectedToken) &&
                Request.Query.TryGetValue("token", out StringValues tokenQuery))
            {
                protectedToken = tokenQuery.ToString();
            }

            if (string.IsNullOrEmpty(protectedToken))
            {
                Logger.LogInformation("No token found in 'Authorization: Bearer <token>', 'Authorization: Basic <base64(ignored:<token>)>', or '?token=<token>'");
                return AuthenticateResult.NoResult();
            }

            if (!TryDecodeToken(protectedToken, out GitHubTokenData token))
            {
                string reportToken = protectedToken;
                if (reportToken.Length > 10)
                {
                    reportToken = reportToken.Substring(0, 5) + "..." + reportToken.Substring(reportToken.Length - 5);
                }

                Logger.LogWarning("Token failed to decode correctly, token signature... {token}", reportToken);
                return AuthenticateResult.Fail("Invalid token");
            }

            if (await _revocation.IsTokenRevokedAsync(token.UserId, token.TokenId))
            {
                Logger.LogWarning("Revoked token used, user {user}, token {token}", token.UserId, token.TokenId);
                return AuthenticateResult.Fail("Invalid token");
            }

            (IEnumerable<Claim> userClaims, IEnumerable<Claim> groupClaims) = await Task.WhenAll(
                _resolver.GetUserInformationClaims(token.AccessToken, Context.RequestAborted),
                _resolver.GetMembershipClaims(token.AccessToken, Context.RequestAborted)
            );

            var identity = new ClaimsIdentity(userClaims.Concat(groupClaims), Scheme.Name);
            var principal = new ClaimsPrincipal(new[] {identity});
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return AuthenticateResult.Success(ticket);
        }

        private string ParseBasicAuth(string authParameter)
        {
            string value = null;
            byte[] rentedBytes = _bytePool.Rent(1000);
            if (Convert.TryFromBase64String(authParameter, rentedBytes, out int size))
            {
                Span<byte> basicBytes = rentedBytes.AsSpan(0, size);

                char[] rentedChars = _charPool.Rent(1000);
                int cb = Encoding.UTF8.GetChars(basicBytes, rentedChars);
                Span<char> basicChars = rentedChars.AsSpan(0, cb);

                int separatorIndex = basicChars.IndexOf(':');
                if (separatorIndex != -1)
                {
                    value = new string(basicChars.Slice(separatorIndex + 1));
                }

                // Clear out the auth stuff, no reason to leave it in memory if we don't have to
                basicChars.Fill('\0');
                basicBytes.Fill(0);

                _charPool.Return(rentedChars);
            }

            _bytePool.Return(rentedBytes);
            return value;
        }

        private bool TryDecodeToken(string protectedToken, out GitHubTokenData token)
        {
            Span<byte> tokenBytes = stackalloc byte[1024];
            if (!Convert.TryFromBase64String(protectedToken, tokenBytes, out int bytesWritten))
            {
                token = default;
                return false;
            }

            // DataProtector can't handle spans, so we have to copy out the value
            var toUnprotect = new byte[bytesWritten];
            tokenBytes.Slice(0, bytesWritten).CopyTo(toUnprotect.AsSpan());

            byte[] unprotected;
            try
            {
                unprotected = _dataProtector.Unprotect(toUnprotect);
            }
            catch (Exception e)
            {
                Logger.LogWarning("IDataProtector.Unprotect exception: {exception}", e);
                token = default;
                return false;
            }

            if (!GitHubTokenEncoder.TryDecodeToken(unprotected, Logger, out GitHubTokenData candidateToken))
            {
                Logger.LogWarning("Malformed token, aborting");
                token = default;
                return false;
            }

            if (candidateToken.Expiration < DateTimeOffset.UtcNow)
            {
                Logger.LogInformation("Expired token used, expired on {expiration}, from user {user}, with id {id}",
                    candidateToken.Expiration,
                    candidateToken.UserId,
                    candidateToken.TokenId);
                token = default;
                return false;
            }

            token = candidateToken;
            return true;
        }

        internal string EncodeToken(ClaimsPrincipal user, StoredTokenData token)
        {
            byte[] encoded = GitHubTokenEncoder.EncodeToken(new GitHubTokenData(token.UserId,
                token.TokenId,
                token.Expiration,
                _resolver.GetAccessToken(user)));

            byte[] prot = _dataProtector.Protect(encoded);

            return Convert.ToBase64String(prot);
        }
    }

    public class UserTokenOptions : AuthenticationSchemeOptions
    {
    }
}
