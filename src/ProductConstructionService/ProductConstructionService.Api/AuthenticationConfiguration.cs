﻿// Licensed to the .NET Foundation under one or more agreements.
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

    // We want to use the AuthenticationScheme for all of our calls
    public const string AuthenticationSchemeRequestPath = "";

    public static void AddAuthentication(this WebApplicationBuilder builder)
    {
        IConfigurationSection gitHubAuthentication = builder.Configuration.GetSection(GitHubAuthenticationKey);

        gitHubAuthentication[GitHubClientIdKey] = builder.Configuration["github-oauth-id"];
        gitHubAuthentication[GitHubClientSecretKey] = builder.Configuration["github-oauth-secret"];

        builder.Services.AddMemoryCache();

        builder.Services.Configure<GitHubClientOptions>(o =>
        {
            o.ProductHeader = new Octokit.ProductHeaderValue("Maestro",
                Assembly.GetEntryAssembly() 
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);
        });
        builder.Services.ConfigureAuthServices(builder.Environment.IsDevelopment(), gitHubAuthentication, AuthenticationSchemeRequestPath);
    }
}
