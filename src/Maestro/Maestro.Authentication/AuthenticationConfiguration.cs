// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Web.Authentication;
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

        var authentication = services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = options.DefaultChallengeScheme = options.DefaultScheme = "Contextual";
                options.DefaultSignInScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddPolicyScheme("Contextual", "Contextual", policyOptions =>
            {
                policyOptions.ForwardDefaultSelector = ctx =>
                    !ctx.Request.Path.StartsWithSegments(authenticationSchemeRequestPath)
                        ? OpenIdConnectDefaults.AuthenticationScheme
                        : JwtBearerDefaults.AuthenticationScheme;
            });

        if (entraAuthConfig?.Exists() ?? false)
        {
            var openIdAuth = services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme);

            openIdAuth
                .AddMicrosoftIdentityWebApi(entraAuthConfig);

            openIdAuth
                .AddMicrosoftIdentityWebApp(entraAuthConfig);
        }

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
