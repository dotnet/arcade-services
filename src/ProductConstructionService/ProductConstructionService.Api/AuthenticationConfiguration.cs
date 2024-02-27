// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Maestro.Authentication;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Web.Authentication.GitHub;

namespace ProductConstructionService.Api;

public static class AuthenticationConfiguration
{
    public const string GitHubAuthenticationKey = "GitHubAuthenticationOptions";
    public const string GitHubClientIdSecret = "github-oauth-id";
    public const string GitHubClientSecretSecret = "github-oauth-secret";

    // We want to use the AuthenticationScheme for all of our calls
    public const string AuthenticationSchemeRequestPath = "";

    public static void AddAuthentication(this WebApplicationBuilder builder)
    {
        GitHubAuthenticationOptions options = new();
        builder.Configuration.GetSection(GitHubAuthenticationKey).Bind(options);

        options.ClientId = builder.Configuration[GitHubClientIdSecret] ?? throw new ArgumentException($"{GitHubClientIdSecret} secret not set");
        options.ClientSecret = builder.Configuration[GitHubClientSecretSecret] ?? throw new ArgumentException($"{GitHubClientSecretSecret} secret not set");

        builder.Services.AddMemoryCache();

        builder.Services.Configure<GitHubClientOptions>(o =>
        {
            o.ProductHeader = new Octokit.ProductHeaderValue("Maestro",
                Assembly.GetEntryAssembly() 
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);
        });
        builder.Services.ConfigureAuthServices(builder.Environment.IsDevelopment(), options, AuthenticationSchemeRequestPath);
    }
}
