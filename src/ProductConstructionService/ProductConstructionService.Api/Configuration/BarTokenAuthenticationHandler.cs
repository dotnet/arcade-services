// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text.Encodings.Web;
using Maestro.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.DotNet.Web.Authentication.AccessToken;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ProductConstructionService.Api.Configuration;

public class BarTokenAuthenticationHandler : AuthenticationHandler<PersonalAccessTokenAuthenticationOptions<ApplicationUser>>
{
    private readonly BuildAssetRegistryContext _dbContext;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private readonly ILogger<BarTokenAuthenticationHandler> _logger;

    [Obsolete]
    public BarTokenAuthenticationHandler(
        BuildAssetRegistryContext context,
        SignInManager<ApplicationUser> signInManager,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IOptionsMonitor<PersonalAccessTokenAuthenticationOptions<ApplicationUser>> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        ISystemClock clock,
        ILogger<BarTokenAuthenticationHandler> logger)
        : base(options, loggerFactory, encoder, clock)
    {
        _dbContext = context;
        _signInManager = signInManager;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            var requestToken = GetToken();
            if (string.IsNullOrEmpty(requestToken))
            {
                return AuthenticateResult.NoResult();
            }

            (int tokenId, string password)? decodedToken = DecodeToken(requestToken);

            if (!decodedToken.HasValue || decodedToken.Value.tokenId == 0 || string.IsNullOrEmpty(decodedToken.Value.password))
            {
                var message = "Failed to decode personal access token";
                _logger.LogInformation(message);
                return AuthenticateResult.Fail(message);
            }

            (var tokenId, var password) = decodedToken.Value;

            ApplicationUserPersonalAccessToken? dbToken = await _dbContext
                .Set<ApplicationUserPersonalAccessToken>()
                .Where(t => t.Id == tokenId)
                .Include(t => t.ApplicationUser)
                .FirstOrDefaultAsync();

            if (dbToken == null)
            {
                return AuthenticateResult.Fail("No existing token found");
            }

            var hash = _passwordHasher.HashPassword(dbToken.ApplicationUser, password);
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
        catch (Exception e)
        {
            _logger.LogDebug(e, "Failed to authenticate personal access token");
        }

        return AuthenticateResult.NoResult();
    }

    private string? GetToken()
    {
        string? authHeader = Request.Headers["Authorization"];
        if (!string.IsNullOrEmpty(authHeader))
        {
            var prefix = "Bearer ";
            if (authHeader.StartsWith(prefix))
            {
                authHeader = authHeader.Substring(prefix.Length).Trim();
            }
        }

        return authHeader;
    }

    private (int tokenId, string password)? DecodeToken(string input)
    {
        var tokenBytes = WebEncoders.Base64UrlDecode(input);
        if (tokenBytes.Length != PersonalAccessTokenUtilities.CalculateTokenSizeForPasswordSize(16))
        {
            return null;
        }

        var tokenId = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(tokenBytes, 0));
        var password = WebEncoders.Base64UrlEncode(tokenBytes, PersonalAccessTokenUtilities.TokenIdByteCount, Options.PasswordSize);
        return (tokenId, password);
    }
}
