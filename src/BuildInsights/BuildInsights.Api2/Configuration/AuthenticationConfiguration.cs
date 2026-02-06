// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;

namespace ProductConstructionService.Api.Configuration;

internal static class AuthenticationConfiguration
{
    public const string EntraAuthorizationSchemeName = "Entra";
    public const string ApiAuthorizationPolicyName = "MsftApi";
    public const string WebAuthorizationPolicyName = "MsftWeb";
    public const string AdminAuthorizationPolicyName = "RequireAdminAccess";

    public const string AccountSignInRoute = "/Account/SignIn";

    public static readonly string[] AuthenticationSchemes =
    [
        EntraAuthorizationSchemeName,
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

        // URI where the BarViz auth loop will come back to.
        // This is needed because the service might run under a different hostname (like the Container App one),
        // whereas we need to redirect to the proper domain (e.g. maestro.dot.net)
        var redirectUri = entraAuthConfig["RedirectUri"];

        var openIdAuth = services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme);

        openIdAuth
            .AddMicrosoftIdentityWebApi(entraAuthConfig, EntraAuthorizationSchemeName);

        openIdAuth
            .AddMicrosoftIdentityWebApp(options =>
            {
                entraAuthConfig.Bind(options);
                if (!string.IsNullOrEmpty(redirectUri))
                {
                    options.Events.OnRedirectToIdentityProvider += context =>
                    {
                        var returnUrl = context.Request.Path + context.Request.QueryString;
                        context.ProtocolMessage.RedirectUri = redirectUri;
                        context.ProtocolMessage.State = Convert.ToBase64String(Encoding.UTF8.GetBytes(returnUrl));
                        return Task.CompletedTask;
                    };

                    options.Events.OnMessageReceived += context =>
                    {
                        // The redirect_uri is set to the one we have in the configuration, but we need to
                        // redirect to the one that was used to authenticate.
                        if (!string.IsNullOrEmpty(context.ProtocolMessage.State))
                        {
                            try
                            {
                                var returnUrl = Encoding.UTF8.GetString(Convert.FromBase64String(context.ProtocolMessage.State));
                                context.Response.Redirect(returnUrl);
                            }
                            catch (FormatException)
                            {
                                // Handle malformed state value gracefully, e.g., redirect to a safe default or log the error
                                // For now, redirect to root
                                context.Response.Redirect("/");
                            }
                        }
                        else
                        {
                            // If no state is provided, redirect to the root
                            context.Response.Redirect("/");
                        }

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
            .AddDefaultPolicy(WebAuthorizationPolicyName, policy =>
                {
                    policy.AddAuthenticationSchemes(AuthenticationSchemes);
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole(userRole, adminRole);
                })
            .AddPolicy(ApiAuthorizationPolicyName, policy =>
                {
                    // Cookie scheme for BarViz, Entra JWT for Darc and other clients
                    // The order matters here as the last scheme's Forbid() handler is used for processing authentication failures
                    // Since cookie scheme returns 200 with the auth exception in the body, Entra should be used instead as it 401s
                    policy.AddAuthenticationSchemes([CookieAuthenticationDefaults.AuthenticationScheme, EntraAuthorizationSchemeName]);
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole(userRole, adminRole);
                })
            .AddPolicy(AdminAuthorizationPolicyName, policy =>
                {
                    policy.AddAuthenticationSchemes(AuthenticationSchemes);
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole(adminRole);
                });
    }
}
