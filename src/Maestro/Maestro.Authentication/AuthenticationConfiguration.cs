// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Maestro.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Web.Authentication;
using Microsoft.DotNet.Web.Authentication.AccessToken;
using Microsoft.DotNet.Web.Authentication.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;

#nullable enable
namespace Maestro.Authentication;

public static class AuthenticationConfiguration
{
    public const string EntraAuthorizationPolicyName = "Entra";
    public const string MsftAuthorizationPolicyName = "msft";

    public const string AccountSignInRoute = "/Account/SignIn";

    public static readonly string[] AuthenticationSchemes =
    [
        EntraAuthorizationPolicyName,
        OpenIdConnectDefaults.AuthenticationScheme,
        PersonalAccessTokenDefaults.AuthenticationScheme,
    ];

    /// <summary>
    /// Sets up authentication and authorization services.
    /// </summary>
    /// <param name="entraAuthConfig">Entra-based auth configuration (or null if turned off)</param>
    public static void ConfigureAuthServices(this IServiceCollection services, IConfigurationSection? entraAuthConfig)
    {
        services.Configure<CookiePolicyOptions>(options =>
        {
            // This lambda determines whether user consent for non-essential cookies is needed for a given request.
            options.CheckConsentNeeded = context => true;
            options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
            // Handling SameSite cookie according to https://docs.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-3.1
            options.HandleSameSiteCookieCompatibility();
        });

        services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, cookieAuthOptions =>
        {
            // Allow /DependencyFlow pages to render for authenticated users in iframe on Azure DevOps dashboard
            // with browsers that support third-party cookies.
            cookieAuthOptions.Cookie.SameSite = SameSiteMode.None;
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
            .AddMicrosoftIdentityWebApi(entraAuthConfig, EntraAuthorizationPolicyName);

        openIdAuth
            .AddMicrosoftIdentityWebApp(entraAuthConfig);

        var authentication = services
            .AddAuthentication(options =>
            {
                options.DefaultSignInScheme = OpenIdConnectDefaults.AuthenticationScheme;
            });

        // Register support for BAR token validation
        authentication.AddScheme<PersonalAccessTokenAuthenticationOptions<ApplicationUser>, BarTokenAuthenticationHandler>(
            PersonalAccessTokenDefaults.AuthenticationScheme,
            configureOptions: null);

        services
            .AddAuthorization(options =>
            {
                options.AddPolicy(MsftAuthorizationPolicyName, policy =>
                {
                    // These roles are still needed for the BAR token validation
                    // When we deprecate the BAR token, we can remove these and keep the entra role validation only
                    var dncengRole = GitHubClaimResolver.GetTeamRole("dotnet", "dnceng");
                    var arcadeContribRole = GitHubClaimResolver.GetTeamRole("dotnet", "arcade-contrib");
                    var prodconSvcsRole = GitHubClaimResolver.GetTeamRole("dotnet", "prodconsvcs");

                    policy.AddAuthenticationSchemes(AuthenticationSchemes);
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(context =>
                    {
                        return context.User.IsInRole(entraRole)
                            || context.User.IsInRole(dncengRole)
                            || context.User.IsInRole(arcadeContribRole)
                            || context.User.IsInRole(prodconSvcsRole);
                    });
                });
            });

        services.Configure<MvcOptions>(
            options =>
            {
                options.Conventions.Add(new DefaultAuthorizeActionModelConvention(MsftAuthorizationPolicyName));
            });
    }
}
