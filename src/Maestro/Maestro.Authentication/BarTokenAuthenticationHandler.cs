// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Net;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.DotNet.Web.Authentication.AccessToken;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#nullable enable
namespace Maestro.Authentication;

public class BarTokenAuthenticationHandler : AuthenticationHandler<PersonalAccessTokenAuthenticationOptions<ApplicationUser>>
{
    private readonly BuildAssetRegistryContext _dbContext;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;

    public BarTokenAuthenticationHandler(
        BuildAssetRegistryContext context,
        SignInManager<ApplicationUser> signInManager,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IOptionsMonitor<PersonalAccessTokenAuthenticationOptions<ApplicationUser>> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
        _dbContext = context;
        _signInManager = signInManager;
        _passwordHasher = passwordHasher;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            string? requestToken = GetToken();
            if (string.IsNullOrEmpty(requestToken))
            {
                return AuthenticateResult.NoResult();
            }

            (int tokenId, string password)? decodedToken = DecodeToken(requestToken);

            if (!decodedToken.HasValue)
            {
                return AuthenticateResult.Fail("Failed to decode personal access token");
            }

            (int tokenId, string password) = decodedToken.Value;

            if (tokenId == 0 || string.IsNullOrEmpty(password))
            {
                return AuthenticateResult.Fail("Failed to decode personal access token");
            }

            ApplicationUserPersonalAccessToken? dbToken = await _dbContext
                .Set<ApplicationUserPersonalAccessToken>()
                .Where(t => t.Id == tokenId)
                .Include(t => t.ApplicationUser)
                .FirstOrDefaultAsync();

            if (dbToken != null)
            {
                string hash = _passwordHasher.HashPassword(dbToken.ApplicationUser, password);
                PasswordVerificationResult result = _passwordHasher.VerifyHashedPassword(dbToken.ApplicationUser, hash, password);

                if (result != PasswordVerificationResult.Success && result != PasswordVerificationResult.SuccessRehashNeeded)
                {
                    return AuthenticateResult.Fail("Invalid personal access token password");
                }

                var ticket = new AuthenticationTicket(await _signInManager.CreateUserPrincipalAsync(dbToken.ApplicationUser), Scheme.Name);
                var userContext = new PersonalAccessTokenValidatePrincipalContext<ApplicationUser>(Context, Scheme, Options, ticket, dbToken.ApplicationUser);
                if (userContext.Principal == null)
                {
                    return AuthenticateResult.Fail("No principal found");
                }
                return AuthenticateResult.Success(new AuthenticationTicket(userContext.Principal, userContext.Properties, Scheme.Name));
            }
        }
        catch
        {
        }

        return AuthenticateResult.NoResult();
    }

    private string? GetToken()
    {
        string? authHeader = Request.Headers["Authorization"];
        if (!string.IsNullOrEmpty(authHeader))
        {
            string prefix = "Bearer ";
            if (authHeader.StartsWith(prefix))
            {
                authHeader = authHeader.Substring(prefix.Length).Trim();
            }
        }

        return authHeader;
    }

    private (int tokenId, string password)? DecodeToken(string input)
    {
        byte[] tokenBytes = WebEncoders.Base64UrlDecode(input);
        if (tokenBytes.Length != PersonalAccessTokenUtilities.CalculateTokenSizeForPasswordSize(16))
        {
            return null;
        }

        int tokenId = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(tokenBytes, 0));
        string password = WebEncoders.Base64UrlEncode(tokenBytes, PersonalAccessTokenUtilities.TokenIdByteCount, Options.PasswordSize);
        return (tokenId, password);
    }
}
