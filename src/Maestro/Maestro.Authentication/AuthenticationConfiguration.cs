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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Web.Authentication;
using Microsoft.DotNet.Web.Authentication.AccessToken;
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
    /// <param name="authenticationSchemeRequestPath">Path of the URI for which we require auth (e.g. "/api")</param>
    /// <param name="entraAuthConfig">Entra-based auth configuration (or null if turned off)</param>
    public static void ConfigureAuthServices(
        this IServiceCollection services,
        bool requirePolicyRole,
        string authenticationSchemeRequestPath,
        IConfigurationSection? entraAuthConfig = null)
    {
        services
            .AddIdentity<ApplicationUser, IdentityRole<int>>(
                options => options.Lockout.AllowedForNewUsers = false)
            .AddEntityFrameworkStores<BuildAssetRegistryContext>();

        services.Configure<CookiePolicyOptions>(options =>
        {
            // This lambda determines whether user consent for non-essential cookies is needed for a given request.
            options.CheckConsentNeeded = context => true;
            options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
            // Handling SameSite cookie according to https://docs.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-3.1
            options.HandleSameSiteCookieCompatibility();
        });

        var authentication = services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = options.DefaultChallengeScheme = options.DefaultScheme = "Contextual";
                options.DefaultSignInScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddPolicyScheme("Contextual", "Contextual", policyOptions =>
            {
                policyOptions.ForwardDefaultSelector = ctx =>
                {
                    if (!ctx.Request.Path.StartsWithSegments(authenticationSchemeRequestPath))
                    {
                        return OpenIdConnectDefaults.AuthenticationScheme;
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

        //services.ConfigureExternalCookie(
        //    options =>
        //    {
        //        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        //        options.ReturnUrlParameter = "returnUrl";
        //        options.LoginPath = AccountSignInRoute;
        //        options.Events = new CookieAuthenticationEvents
        //        {
        //            OnRedirectToLogin = ctx =>
        //            {
        //                if (ctx.Request.Path.StartsWithSegments("/api"))
        //                {
        //                    ctx.Response.StatusCode = 401;
        //                    return Task.CompletedTask;
        //                }

        //                ctx.Response.Redirect(ctx.RedirectUri);
        //                return Task.CompletedTask;
        //            },
        //            OnRedirectToAccessDenied = ctx =>
        //            {
        //                ctx.Response.StatusCode = 403;
        //                return Task.CompletedTask;
        //            },
        //        };
        //    });

        //services.ConfigureApplicationCookie(
        //    options =>
        //    {
        //        options.ExpireTimeSpan = LoginCookieLifetime;
        //        options.SlidingExpiration = true;
        //        options.ReturnUrlParameter = "returnUrl";
        //        options.LoginPath = AccountSignInRoute;
        //        options.Events = new CookieAuthenticationEvents
        //        {
        //            OnSigningIn = async ctx =>
        //            {
        //                var dbContext = ctx.HttpContext.RequestServices
        //                    .GetRequiredService<BuildAssetRegistryContext>();
        //                var signInManager = ctx.HttpContext.RequestServices
        //                    .GetRequiredService<SignInManager<ApplicationUser>>();
        //                var userManager = ctx.HttpContext.RequestServices
        //                    .GetRequiredService<UserManager<ApplicationUser>>();
        //                ExternalLoginInfo info = await signInManager.GetExternalLoginInfoAsync();

        //                var user = await userManager.GetUserAsync(ctx.Principal);
        //                await UpdateUserTokenAsync(dbContext, userManager, user, info);

        //                IdentityOptions identityOptions = ctx.HttpContext.RequestServices
        //                    .GetRequiredService<IOptions<IdentityOptions>>()
        //                    .Value;

        //                // replace the ClaimsPrincipal we are about to serialize to the cookie with a reference
        //                Claim claim = ctx.Principal!.Claims.First(
        //                    c => c.Type == identityOptions.ClaimsIdentity.UserIdClaimType);
        //                Claim[] claims = [claim];
        //                var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
        //                ctx.Principal = new ClaimsPrincipal(identity);
        //            },
        //            OnValidatePrincipal = async ctx =>
        //            {
        //                var userManager = ctx.HttpContext.RequestServices
        //                    .GetRequiredService<UserManager<ApplicationUser>>();
        //                var signInManager = ctx.HttpContext.RequestServices
        //                    .GetRequiredService<SignInManager<ApplicationUser>>();

        //                // extract the userId from the ClaimsPrincipal and read the user from the Db
        //                ApplicationUser user = await userManager.GetUserAsync(ctx.Principal);
        //                if (user == null)
        //                {
        //                    ctx.RejectPrincipal();
        //                }
        //                else
        //                {
        //                    ClaimsPrincipal principal = await signInManager.CreateUserPrincipalAsync(user);
        //                    ctx.ReplacePrincipal(principal);
        //                }
        //            }
        //        };
        //    });

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
}
