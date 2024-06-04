// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Authentication;
using Microsoft.DotNet.Web.Authentication.GitHub;

namespace ProductConstructionService.Api.Configuration;

public static class AuthenticationConfiguration
{
    public const string GitHubAuthenticationKey = "GitHubAuthentication";
    public const string EntraAuthenticationKey = "EntraAuthentication";

    // The ConfigureAuthServices we're using has a parameter that tells the service which Authentication scheme to use
    // If an endpoints path matches the AuthenticationSchemeRequestPath, it will use the authentication scheme, otherwise, it will use the
    // Application scheme. We always want to use the Authentication scheme, so we're setting the path to an empty string
    private static readonly string AuthenticationSchemeRequestPath = string.Empty;

    public static void AddEndpointAuthentication(this WebApplicationBuilder builder, bool requirePolicyRole)
    {
        builder.Services.AddMemoryCache();

        // TODO: https://github.com/dotnet/arcade-services/issues/3351
        IConfigurationSection gitHubAuthentication = builder.Configuration.GetSection(GitHubAuthenticationKey);

        gitHubAuthentication[nameof(GitHubAuthenticationOptions.ClientId)]
            = builder.Configuration.GetRequiredValue(PcsConfiguration.GitHubClientId);
        gitHubAuthentication[nameof(GitHubAuthenticationOptions.ClientSecret)]
            = builder.Configuration.GetRequiredValue(PcsConfiguration.GitHubClientSecret);

        IConfigurationSection entraAuthentication = builder.Configuration.GetSection(EntraAuthenticationKey);

        builder.Services.ConfigureAuthServices(
            requirePolicyRole,
            gitHubAuthentication,
            AuthenticationSchemeRequestPath,
            entraAuthentication);
    }
}
