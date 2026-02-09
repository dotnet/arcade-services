// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Microsoft.DotNet.GitHub.Authentication;

namespace BuildInsights.Api.Configuration;

internal static class GitHubClientFactoryConfiguration
{
    public static void AddGitHubClientFactory(
        this WebApplicationBuilder builder,
        string? appId,
        string? appSecret)
    {
        builder.Services.Configure<GitHubClientOptions>(o =>
        {
            o.ProductHeader = new Octokit.ProductHeaderValue(
                "BuildInsights",
                Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);
        });

        builder.Services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        builder.Services.Configure<GitHubTokenProviderOptions>(o =>
        {
            o.GitHubAppId = !string.IsNullOrEmpty(appId) ? int.Parse(appId) : 0;
            o.PrivateKey = appSecret;
        });
    }
}
