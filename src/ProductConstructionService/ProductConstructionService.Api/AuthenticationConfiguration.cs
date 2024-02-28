// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Maestro.Authentication;
using Microsoft.DotNet.GitHub.Authentication;

namespace ProductConstructionService.Api;

public static class AuthenticationConfiguration
{
    public const string GitHubAuthenticationKey = "GitHubAuthentication";
    public const string GitHubClientIdKey = "ClientId";
    public const string GitHubClientSecretKey = "ClientSecret";

    // The ConfigureAuthServices we're using has a parameter that tells the service which Authentication scheme to use
    // If an endpoints path matches the AuthenticationSchemeRequestPath, it will use the authentication scheme, otherwise, it will use the
    // Application scheme. We always want to use the Authentication scheme, so we're setting the path to an empty string
    private static readonly string AuthenticationSchemeRequestPath = string.Empty;

    public static void AddAuthentication(this WebApplicationBuilder builder)
    {
        // TODO: https://github.com/dotnet/arcade-services/issues/3351
        IConfigurationSection gitHubAuthentication = builder.Configuration.GetSection(GitHubAuthenticationKey);

        gitHubAuthentication[GitHubClientIdKey] = builder.Configuration["github-oauth-id"];
        gitHubAuthentication[GitHubClientSecretKey] = builder.Configuration["github-oauth-secret"];

        builder.Services.AddMemoryCache();

        builder.Services.Configure<GitHubClientOptions>(o =>
        {
            o.ProductHeader = new Octokit.ProductHeaderValue("PCS",
                Assembly.GetEntryAssembly() 
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);
        });
        builder.Services.ConfigureAuthServices(builder.Environment.IsDevelopment(), gitHubAuthentication, AuthenticationSchemeRequestPath);
    }
}
