// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.GitHubGraphQL;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();
var knowIssueMonitor = builder.Services
    .RegisterServices()
    .ConfigureServices()
    .BuildServiceProvider()
    .CreateScope()
    .ServiceProvider
    .GetRequiredService<IKnownIssueMonitor>();

await knowIssueMonitor.RunAsync();

internal static class Configuration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        // TODO
        //(string assemblyName, string assemblyVersion) = Helpers.GetAssemblyVersion();

        services.AddDefaultJsonConfiguration();
        services.Configure<GitHubIssuesSettings>("GitHubIssuesSettings", (o, c) => c.Bind(o));
        services.Configure<KnownIssuesProjectOptions>("KnownIssuesProjectOptions", (o, c) => c.Bind(o));
        services.Configure<KustoOptions>("Kusto", (o, c) => c.Bind(o));
        services.Configure<SsaCriteriaSettings>("SsaCriteriaSettings", (o, c) => c.Bind(o));

        // TODO
        // GitHub will reject authenticated requests with a 403 Forbidden if the UserAgent isn't set
        //services.Configure<HttpClientFactoryOptions>(options =>
        //{
        //    options.HttpClientActions.Add(client =>
        //    {
        //        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(assemblyName, assemblyVersion));
        //    });
        //});

        return services;
    }

    public static IServiceCollection RegisterServices(this IServiceCollection services)
    {
        services.AddHttpClient();

        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.AddSingleton<IGitHubApplicationClientFactory, GitHubApplicationClientFactory>();
        services.AddSingleton<IInstallationLookup, InMemoryCacheInstallationLookup>();
        services.AddGitHubTokenProvider();
        services.AddSingleton(_ => TimeProvider.System);

        services.AddSingleton<IGitHubGraphQLClient, GitHubGraphQLClient>();
        services.AddSingleton<IGitHubGraphQLHttpClientFactory, GitHubGraphQLAppHttpClientFactory>();

        services.AddKustoClientProvider("Kusto");
        services.AddSingleton(p => p.GetRequiredService<IKustoIngestClientFactory>().GetClient());
        services.AddSingleton<IKustoIngestClientFactory, KustoIngestClientFactory>();

        services.AddSingleton<IGitHubIssuesService, GitHubIssuesProvider>();
        services.AddSingleton<IKnownIssuesService, KnownIssuesProvider>();
        services.AddSingleton<IKnownIssuesHistoryService, KnownIssuesHistoryProvider>();
        services.AddSingleton<IKnownIssueMonitor, KnownIssueMonitor>();
        services.AddSingleton<KnownIssuesReportHelper>();

        return services;
    }
}
