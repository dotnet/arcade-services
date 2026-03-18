// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace BuildInsights.ServiceDefaults.Configuration;

internal static class GitHubClientFactoryConfiguration
{
    public static void AddGitHubClientFactory(
        this IServiceCollection services,
        int? appId,
        string? appSecret)
    {
        services.Configure<GitHubClientOptions>(o =>
        {
            o.ProductHeader = new Octokit.ProductHeaderValue(
                "BuildInsights",
                Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion);
        });

        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.Configure<GitHubTokenProviderOptions>(o =>
        {
            o.GitHubAppId = appId ?? 0;
            o.PrivateKey = appSecret;
        });
    }
}
