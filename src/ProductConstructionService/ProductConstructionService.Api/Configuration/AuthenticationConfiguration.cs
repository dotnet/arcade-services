// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;

namespace ProductConstructionService.Api.Configuration;

internal static class AuthenticationConfiguration
{
    public const string EntraAuthorizationPolicyName = "Entra";
    public const string MsftAuthorizationPolicyName = "msft";
    public const string AdminAuthorizationPolicyName = "RequireAdminAccess";

    public const string AccountSignInRoute = "/Account/SignIn";

    public static readonly string[] AuthenticationSchemes =
    [
        EntraAuthorizationPolicyName,
        OpenIdConnectDefaults.AuthenticationScheme,
    ];

    /// <summary>
    /// Sets up authentication and authorization services.
    /// </summary>
    /// <param name="services">The service collection</param>
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

        // Register Entra based authentication
        if (!entraAuthConfig.Exists())
        {
            throw new Exception("Entra authentication is missing in configuration");
        }

        var redirectUri = entraAuthConfig["RedirectUri"];
        var openIdAuth = services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme);

        openIdAuth
            .AddMicrosoftIdentityWebApi(entraAuthConfig, EntraAuthorizationPolicyName);

        openIdAuth
            .AddMicrosoftIdentityWebApp(options =>
            {
                entraAuthConfig.Bind(options);
                if (!string.IsNullOrEmpty(redirectUri))
                {
                    // URI where the BarViz auth loop will come back to.
                    // This is needed because the service might run under a different hostname (like the Container App one),
                    // whereas we need to redirect to the proper domain (e.g. maestro.dot.net)
                    options.Events.OnRedirectToIdentityProvider += context =>
                    {
                        context.ProtocolMessage.RedirectUri = redirectUri;
                        return Task.CompletedTask;
                    };
                }
            });

        var userRole = entraAuthConfig["UserRole"]
            ?? throw new Exception("Expected 'UserRole' to be set in the Entra configuration containing " +
                                   "a role on the application granted to API users");
        var adminRole = entraAuthConfig["AdminRole"]
            ?? throw new Exception("Expected 'AdminRole' to be set in the Entra configuration containing " +
                                   "a role on the application granted to API users");

        services
            .AddAuthentication(options =>
            {
                options.DefaultSignInScheme = OpenIdConnectDefaults.AuthenticationScheme;
            });

        services
            .AddAuthorizationBuilder()
            .AddPolicy(MsftAuthorizationPolicyName, policy =>
                {
                    policy.AddAuthenticationSchemes(AuthenticationSchemes);
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole(userRole);
                })
            .AddPolicy(AdminAuthorizationPolicyName, policy =>
                {
                    policy.AddAuthenticationSchemes(AuthenticationSchemes);
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole(adminRole);
                });
    }
}
