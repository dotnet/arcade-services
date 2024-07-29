// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Maestro.Data;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Web.Authentication;
using Microsoft.DotNet.Web.Authentication.AccessToken;
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
    /// <param name="authenticationSchemeRequestPath">Path of the URI for which we require auth (e.g. "/api")</param>
    /// <param name="entraAuthConfig">Entra-based auth configuration (or null if turned off)</param>
    public static void ConfigureAuthServices(
        this IServiceCollection services,
        string authenticationSchemeRequestPath,
        IConfigurationSection? entraAuthConfig)
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
        if (!entraAuthConfig.Exists())
        {
            throw new Exception("Entra authentication is missing in configuration");
        }

        string entraRole = entraAuthConfig["UserRole"]
            ?? throw new Exception("Expected 'UserRole' to be set in the Entra configuration containing " +
                                   "a role on the application granted to API users");

        var openIdAuth = services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme);

        openIdAuth
            .AddMicrosoftIdentityWebApi(entraAuthConfig, "Bearer");

        openIdAuth
            .AddMicrosoftIdentityWebApp(entraAuthConfig);

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
                        ? "Bearer"
                        : PersonalAccessTokenDefaults.AuthenticationScheme;
                };
            });

        // Register support for BAR token validation
        authentication
            .AddScheme<PersonalAccessTokenAuthenticationOptions<ApplicationUser>, BarTokenAuthenticationHandler>(PersonalAccessTokenDefaults.AuthenticationScheme, null);

        services.AddAuthorization(
            options =>
            {
                options.AddPolicy(
                    MsftAuthorizationPolicyName,
                    policy =>
                    {
                        policy.RequireAuthenticatedUser();
                        policy.RequireAssertion(context => context.User.IsInRole(entraRole));
                    });
            });

        services.Configure<MvcOptions>(
            options =>
            {
                options.Conventions.Add(new DefaultAuthorizeActionModelConvention(MsftAuthorizationPolicyName));
            });
    }
}
