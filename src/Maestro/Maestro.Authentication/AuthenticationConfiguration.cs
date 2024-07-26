// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Security.Claims;
using Maestro.Data;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Web.Authentication;
using Microsoft.DotNet.Web.Authentication.AccessToken;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;

#nullable enable
namespace Maestro.Authentication;

public static class AuthenticationConfiguration
{
    public const string MsftAuthorizationPolicyName = "msft";

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
        services.Configure<CookiePolicyOptions>(options =>
        {
            // This lambda determines whether user consent for non-essential cookies is needed for a given request.
            options.CheckConsentNeeded = context => true;
            options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
            // Handling SameSite cookie according to https://docs.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-3.1
            options.HandleSameSiteCookieCompatibility();
        });

        // Support for old Maestro tokens
        services
            .AddIdentity<ApplicationUser, IdentityRole<int>>(
                options => options.Lockout.AllowedForNewUsers = false)
            .AddEntityFrameworkStores<BuildAssetRegistryContext>();

        // Register Entra based authentication
        if (entraAuthConfig?.Exists() ?? false)
        {
            var openIdAuth = services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme);

            openIdAuth
                .AddMicrosoftIdentityWebApi(entraAuthConfig);

            openIdAuth
                .AddMicrosoftIdentityWebApp(entraAuthConfig);
        }

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
                    return authHeader == null || (authHeader?.Length > 100 && authHeader.ToLower().StartsWith("bearer ey"))
                        ? OpenIdConnectDefaults.AuthenticationScheme
                        : PersonalAccessTokenDefaults.AuthenticationScheme;
                };
            });

        // Support for old Maestro tokens
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
                            var dbContext = context.HttpContext.RequestServices
                                .GetRequiredService<BuildAssetRegistryContext>();
                            var signInManager = context.HttpContext.RequestServices
                                .GetRequiredService<SignInManager<ApplicationUser>>();

                            ClaimsPrincipal principal = await signInManager.CreateUserPrincipalAsync(context.User);
                            context.ReplacePrincipal(principal);
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
}
