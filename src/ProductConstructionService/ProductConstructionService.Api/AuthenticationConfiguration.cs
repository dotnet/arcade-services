// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Authentication;
using Maestro.DataProviders;

namespace ProductConstructionService.Api;

public static class AuthenticationConfiguration
{
    public const string GitHubAuthenticationKey = "GitHubAuthentication";

    public static void AddAuthentication(this WebApplicationBuilder builder)
    {
        IConfigurationSection gitHubAuthentication = builder.Configuration.GetSection(GitHubAuthenticationKey);

        gitHubAuthentication["ClientId"] = builder.Configuration["github-oauth-id"];
        gitHubAuthentication["ClientSecret"] = builder.Configuration["github-oauth-secret"];

        builder.Services.AddSingleton<DarcRemoteMemoryCache>();

        builder.Services.ConfigureAuthServices(builder.Environment.IsDevelopment(), gitHubAuthentication);
    }
}
