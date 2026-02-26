// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace BuildInsights.GitHubGraphQL;

public static class GitHubGraphQLConfiguration
{
    public static IServiceCollection AddGitHubGraphQL(this IServiceCollection services)
    {
        services.AddTransient<IGitHubGraphQLClient, GitHubGraphQLClient>();
        services.AddTransient<IGitHubGraphQLHttpClientFactory, GitHubGraphQLAppHttpClientFactory>();

        // GitHub will reject authenticated requests with a 403 Forbidden if the UserAgent isn't set
        var assembly = typeof(GitHubGraphQLConfiguration).Assembly;
        var assemblyName = assembly.GetName().Name;
        var assemblyVersion = assembly.GetName().Version.ToString();
        services.Configure<HttpClientFactoryOptions>(options =>
        {
            options.HttpClientActions.Add(client =>
            {
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(assemblyName, assemblyVersion));
            });
        });

        return services;
    }
}
