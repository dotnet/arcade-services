// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.DotNet.Web.Authentication.AccessToken;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#nullable enable
namespace Maestro.Authentication;

public class BarTokenAuthenticationHandler : AuthenticationHandler<PersonalAccessTokenAuthenticationOptions<ApplicationUser>>
{
    private readonly BuildAssetRegistryContext _context;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public BarTokenAuthenticationHandler(
        BuildAssetRegistryContext context,
        SignInManager<ApplicationUser> signInManager,
        IOptionsMonitor<PersonalAccessTokenAuthenticationOptions<ApplicationUser>> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
        _context = context;
        _signInManager = signInManager;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            string? token = GetToken();
            if (string.IsNullOrEmpty(token))
            {
                return AuthenticateResult.NoResult();
            }

            ApplicationUserPersonalAccessToken? user = await _context
                .Set<ApplicationUserPersonalAccessToken>()
                .Where(t => t.Hash == token)
                .Include(t => t.ApplicationUser)
                .FirstOrDefaultAsync();

            if (user != null)
            {
                var ticket = new AuthenticationTicket(await _signInManager.CreateUserPrincipalAsync(user.ApplicationUser), Scheme.Name);
                var userContext = new PersonalAccessTokenValidatePrincipalContext<ApplicationUser>(Context, Scheme, Options, ticket, user.ApplicationUser);
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
}
