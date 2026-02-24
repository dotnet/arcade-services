// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.GitHub;

public static class GitHubConfiguration
{
    public static IServiceCollection AddGitHub(this IServiceCollection services)
    {
        services.TryAddScoped<IGitHubApplicationClientFactory, GitHubApplicationClientFactory>();
        services.TryAddTransient<IGitHubPullRequestService, GitHubPullRequestProvider>();
        services.TryAddTransient<IGitHubChecksService, GitHubChecksProvider>();
        services.TryAddTransient<IGitHubRepositoryService, GithubRepositoryProvider>();
        services.TryAddTransient<IGitHubClientFactory, GitHubClientFactory>();

        return services;
    }
}
