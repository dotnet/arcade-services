// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues.Models;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.KnownIssues;

public static class KnownIssuesConfiguration
{
    public static IServiceCollection AddKnownIssues(this IServiceCollection services)
    {
        services.TryAddScoped<IKnownIssuesHistoryService, KnownIssuesHistoryProvider>();
        services.TryAddScoped<IKnownIssuesMatchService, KnownIssuesMatchProvider>();
        services.TryAddScoped<IKnownIssuesService, KnownIssuesProvider>();

        // TODO
        services.Configure<KnownIssueUrlOptions>("KnownIssueUrlOptions", (o, c) => c.Bind(o));
        services.Configure<KnownIssuesAnalysisLimits>("KnownIssuesAnalysisLimits", (o, c) => c.Bind(o));
        services.Configure<KustoOptions>("KnownIssuesKustoOptions", (o, c) => c.Bind(o));

        return services;
    }
}
