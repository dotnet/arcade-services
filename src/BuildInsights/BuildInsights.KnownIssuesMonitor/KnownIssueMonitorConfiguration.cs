// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using BuildInsights.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BuildInsights.KnownIssuesMonitor;

public static class KnownIssueMonitorConfiguration
{
    public static class ConfigurationKeys
    {
        // Configuration from appsettings.json
        public const string KnownIssuesProject = "KnownIssuesProject";
        public const string KnownIssuesCreation = "KnownIssuesCreation";
        public const string KnownIssuesAnalysisLimits = "KnownIssuesAnalysisLimits";
        public const string KnownIssuesKusto = "KnownIssuesKusto";
        public const string GitHubIssues = "GitHubIssues";
        public const string InternalProject = "InternalProject";
        public const string SsaCriteriaSettings = "SsaCriteriaSettings";
    }

    public static async Task<IServiceCollection> ConfigureKnownIssueMonitor(this IHostApplicationBuilder builder)
    {
        var knownIssuesCreationConfig = builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesCreation);
        var knownIssuesAnalysisLimitsConfig = builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesAnalysisLimits);
        var knownIssuesKustoConfig = builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesKusto);
        var gitHubIssuesConfig = builder.Configuration.GetSection(ConfigurationKeys.GitHubIssues);

        builder.Services.Configure<KnownIssuesProjectOptions>(ConfigurationKeys.KnownIssuesProject, (o, s) => s.Bind(o));
        builder.Services.Configure<InternalProjectSettings>(ConfigurationKeys.InternalProject, (o, s) => s.Bind(o));
        builder.Services.Configure<SsaCriteriaSettings>(ConfigurationKeys.SsaCriteriaSettings, (o, s) => s.Bind(o));

        await builder.ConfigureBuildInsightsDependencies(addKeyVault: true);
        builder.Services.AddKnownIssues(
            knownIssuesCreationConfig,
            knownIssuesAnalysisLimitsConfig,
            knownIssuesKustoConfig,
            gitHubIssuesConfig);

        builder.Services.TryAddTransient<KnownIssuesReportHelper>();
        builder.Services.TryAddTransient<KnownIssueMonitor>();

        return builder.Services;
    }
}
