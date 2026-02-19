// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace BuildInsights.GitHubGraphQL;

public static class GitHubGraphQLConfiguration
{
    public static IServiceCollection AddGitHubGraphQL(this IServiceCollection services)
    {
        services.AddTransient<IGitHubGraphQLClient, GitHubGraphQLClient>();
        services.AddTransient<IGitHubGraphQLHttpClientFactory, GitHubGraphQLAppHttpClientFactory>();
        return services;
    }
}
