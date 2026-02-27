// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssues.Models;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.KnownIssues;

public static class KnownIssuesConfiguration
{
    public static IServiceCollection AddKnownIssues(
        this IServiceCollection services,
        IConfigurationSection knownIssuesCreationConfiguration,
        IConfigurationSection knownIssuesAnalysisLimitsConfiguration,
        IConfigurationSection knownIssuesKustoConfiguration,
        IConfigurationSection gitHubIssuesConfig)
    {
        services.TryAddScoped<IKnownIssuesHistoryService, KnownIssuesHistoryProvider>();
        services.TryAddScoped<IKnownIssuesMatchService, KnownIssuesMatchProvider>();
        services.TryAddScoped<IKnownIssuesService, KnownIssuesProvider>();
        services.TryAddScoped<IGitHubIssuesService, GitHubIssuesProvider>();

        services.Configure<KnownIssueUrlOptions>(knownIssuesCreationConfiguration);
        services.Configure<KnownIssuesAnalysisLimits>(knownIssuesAnalysisLimitsConfiguration);
        services.Configure<KustoOptions>(knownIssuesKustoConfiguration);
        services.Configure<GitHubIssuesSettings>(gitHubIssuesConfig);

        return services;
    }
}
