// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Web.Authentication;
using Microsoft.DotNet.Web.Authentication.AccessToken;
using Microsoft.DotNet.Web.Authentication.GitHub;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

#nullable enable
namespace Maestro.Authentication;

public static class AuthenticationConfiguration
{
    public const string MsftAuthorizationPolicyName = "msft";

    public static readonly TimeSpan LoginCookieLifetime = TimeSpan.FromMinutes(30);

    public const string AccountSignInRoute = "/Account/SignIn";

    /// <summary>
    /// Sets up authentication and authorization services.
    /// </summary>
    /// <param name="requirePolicyRole">Should we require the @dotnet/dnceng or @dotnet/arcade-cotrib team?</param>
    /// <param name="githubAuthConfig">GitHub auth configuration</param>
    /// <param name="authenticationSchemeRequestPath">Path of the URI for which we require auth (e.g. "/api")</param>
    /// <param name="entraAuthConfig">Entra-based auth configuration (or null if turned off)</param>
    public static void ConfigureAuthServices(
        this IServiceCollection services,
        bool requirePolicyRole,
        IConfigurationSection githubAuthConfig,
        string authenticationSchemeRequestPath,
        IConfigurationSection? entraAuthConfig = null)
    {
        services
            .AddIdentity<ApplicationUser, IdentityRole<int>>(
                options => options.Lockout.AllowedForNewUsers = false)
            .AddEntityFrameworkStores<BuildAssetRegistryContext>();

        var authentication = services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = options.DefaultChallengeScheme = options.DefaultScheme = "Contextual";
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddPolicyScheme("Contextual", "Contextual", policyOptions =>
            {
                policyOptions.ForwardDefaultSelector = ctx =>
                {
                    if (!ctx.Request.Path.StartsWithSegments(authenticationSchemeRequestPath))
                    {
                        return IdentityConstants.ApplicationScheme;
                    }

                    // This is a really simple and a bit hacky (but temporary) quick way to tell between BAR and Entra tokens
                    string? authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
                    return authHeader?.Length > 100 && authHeader.ToLower().StartsWith("bearer ey")
                        ? JwtBearerDefaults.AuthenticationScheme
                        : PersonalAccessTokenDefaults.AuthenticationScheme;
                };
            });

        if (entraAuthConfig?.Exists() ?? false)
        {
            var openIdAuth = services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme);

            openIdAuth
                .AddMicrosoftIdentityWebApi(entraAuthConfig);

            openIdAuth
                .AddMicrosoftIdentityWebApp(entraAuthConfig);
        }

        authentication
            .AddPersonalAccessToken<ApplicationUser>(
                options =>
                {
                    options.Events = new PersonalAccessTokenEvents<ApplicationUser>
                    {
                        OnSetTokenHash = async context =>
                        {
                            var dbContext = context.HttpContext.RequestServices
                                .GetRequiredService<BuildAssetRegistryContext>();
                            int userId = context.User.Id;
                            var token = new ApplicationUserPersonalAccessToken
                            {
                                ApplicationUserId = userId,
                                Name = context.Name,
                                Hash = context.Hash,
                                Created = DateTimeOffset.UtcNow
                            };
                            await dbContext.Set<ApplicationUserPersonalAccessToken>().AddAsync(token);
                            await dbContext.SaveChangesAsync();

                            return token.Id;
                        },
                        OnGetTokenHash = async context =>
                        {
                            var dbContext = context.HttpContext.RequestServices
                                .GetRequiredService<BuildAssetRegistryContext>();
                            ApplicationUserPersonalAccessToken? token = await dbContext
                                .Set<ApplicationUserPersonalAccessToken>()
                                .Where(t => t.Id == context.TokenId)
                                .Include(t => t.ApplicationUser)
                                .FirstOrDefaultAsync();
                            if (token != null)
                            {
                                context.Success(token.Hash, token.ApplicationUser);
                            }
                        },
                        OnValidatePrincipal = async context =>
                        {
                            var signInManager = context.HttpContext.RequestServices
                                .GetRequiredService<SignInManager<ApplicationUser>>();

                            ClaimsPrincipal principal = await signInManager.CreateUserPrincipalAsync(context.User);
                            context.ReplacePrincipal(principal);
                        }
                    };
                });

        services.ConfigureExternalCookie(
            options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                options.ReturnUrlParameter = "returnUrl";
                options.LoginPath = AccountSignInRoute;
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/api"))
                        {
                            ctx.Response.StatusCode = 401;
                            return Task.CompletedTask;
                        }

                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = ctx =>
                    {
                        ctx.Response.StatusCode = 403;
                        return Task.CompletedTask;
                    },
                };
            });
        services.ConfigureApplicationCookie(
            options =>
            {
                options.ExpireTimeSpan = LoginCookieLifetime;
                options.SlidingExpiration = true;
                options.ReturnUrlParameter = "returnUrl";
                options.LoginPath = AccountSignInRoute;
                options.Events = new CookieAuthenticationEvents
                {
                    OnSigningIn = async ctx =>
                    {
                        var dbContext = ctx.HttpContext.RequestServices
                            .GetRequiredService<BuildAssetRegistryContext>();
                        var signInManager = ctx.HttpContext.RequestServices
                            .GetRequiredService<SignInManager<ApplicationUser>>();
                        var userManager = ctx.HttpContext.RequestServices
                            .GetRequiredService<UserManager<ApplicationUser>>();
                        ExternalLoginInfo info = await signInManager.GetExternalLoginInfoAsync();

                        var user = await userManager.GetUserAsync(ctx.Principal);
                        await UpdateUserTokenAsync(dbContext, userManager, user, info);

                        IdentityOptions identityOptions = ctx.HttpContext.RequestServices
                            .GetRequiredService<IOptions<IdentityOptions>>()
                            .Value;

                        // replace the ClaimsPrincipal we are about to serialize to the cookie with a reference
                        Claim claim = ctx.Principal!.Claims.First(
                            c => c.Type == identityOptions.ClaimsIdentity.UserIdClaimType);
                        Claim[] claims = [claim];
                        var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
                        ctx.Principal = new ClaimsPrincipal(identity);
                    },
                    OnValidatePrincipal = async ctx =>
                    {
                        var userManager = ctx.HttpContext.RequestServices
                            .GetRequiredService<UserManager<ApplicationUser>>();
                        var signInManager = ctx.HttpContext.RequestServices
                            .GetRequiredService<SignInManager<ApplicationUser>>();

                        // extract the userId from the ClaimsPrincipal and read the user from the Db
                        ApplicationUser user = await userManager.GetUserAsync(ctx.Principal);
                        if (user == null)
                        {
                            ctx.RejectPrincipal();
                        }
                        else
                        {
                            ClaimsPrincipal principal = await signInManager.CreateUserPrincipalAsync(user);
                            ctx.ReplacePrincipal(principal);
                        }
                    }
                };
            });

        // While entra is optional, we only verify the role when it's available in configuration
        // When it's disabled, we create a random GUID policy that will be never satisfied
        string entraRole = entraAuthConfig?.Exists() ?? false
            ? entraAuthConfig["UserRole"] ?? throw new Exception("Expected 'UserRole' to be set in the AzureAd configuration - " +
                                                                 "a role on the application granted to API users")
            : Guid.NewGuid().ToString();

        services.AddAuthorization(
            options =>
            {
                options.AddPolicy(
                    MsftAuthorizationPolicyName,
                    policy =>
                    {
                        policy.RequireAuthenticatedUser();
                        if (requirePolicyRole)
                        {
                            policy.RequireAssertion(context => context.User.IsInRole(entraRole));
                        }
                    });
            });

        services.Configure<MvcOptions>(
            options =>
            {
                options.Conventions.Add(new DefaultAuthorizeActionModelConvention(MsftAuthorizationPolicyName));
            });
    }

    public static bool ShouldAddClaimToUser(Claim c)
    {
        return c.Type == ClaimTypes.Email || c.Type == "urn:github:name" || c.Type == "urn:github:url" || c.Type == ClaimTypes.Role;
    }

    private static async Task UpdateUserTokenAsync(BuildAssetRegistryContext dbContext,
        UserManager<ApplicationUser> userManager, ApplicationUser user, ExternalLoginInfo info)
    {
        try
        {
            await userManager.SetAuthenticationTokenAsync(
                user,
                info.LoginProvider,
                "access_token",
                info.AuthenticationTokens.First(t => t.Name == "access_token").Value);
        }
        catch (DbUpdateConcurrencyException)
        {
            // If we have a concurrent modification exception that means another request updated this token, we can abandon our update and reload the data from the DB
            foreach (EntityEntry entry in dbContext.ChangeTracker.Entries())
            {
                await entry.ReloadAsync();
            }
        }
    }
}
