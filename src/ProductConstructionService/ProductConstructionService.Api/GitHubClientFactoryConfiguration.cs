// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Microsoft.DotNet.GitHub.Authentication;

namespace ProductConstructionService.Api;

public static class GitHubClientFactoryConfiguration
{
    public const string GitHubAgentNameKey = $"{AuthenticationConfiguration.GitHubAuthenticationKey}:UserAgentProduct";

    public static void AddGitHubClientFactory(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<GitHubClientOptions>(o =>
        {
            o.ProductHeader = new Octokit.ProductHeaderValue(
                builder.Configuration.GetRequiredValue(GitHubAgentNameKey),
                Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);
        });
        builder.Services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();

    }

}
