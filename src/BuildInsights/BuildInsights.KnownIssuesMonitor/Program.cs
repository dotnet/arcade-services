// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssues;
using BuildInsights.ServiceDefaults;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();
await builder.ConfigureKnownIssueMonitor();

var knowIssueMonitor = builder.Services
    .BuildServiceProvider()
    .CreateScope()
    .ServiceProvider
    .GetRequiredService<IKnownIssueMonitor>();

await knowIssueMonitor.RunAsync();

internal static class KnownIssueConfiguration
{
    internal static class ConfigurationKeys
    {
        // Configuration from appsettings.json
        public const string KnownIssuesProject = "KnownIssuesProject";
        public const string KnownIssuesCreation = "KnownIssuesCreation";
        public const string KnownIssuesAnalysisLimits = "KnownIssuesAnalysisLimits";
        public const string KnownIssuesKusto = "KnownIssuesKusto";
        public const string GitHubIssues = "GitHubIssues";
    }

    public static async Task<IServiceCollection> ConfigureKnownIssueMonitor(this IHostApplicationBuilder builder)
    {
        var knownIssuesCreationConfig = builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesCreation);
        var knownIssuesAnalysisLimitsConfig = builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesAnalysisLimits);
        var knownIssuesKustoConfig = builder.Configuration.GetSection(ConfigurationKeys.KnownIssuesKusto);
        var gitHubIssuesConfig = builder.Configuration.GetSection(ConfigurationKeys.GitHubIssues);

        await builder.ConfigureService(addKeyVault: true);
        builder.Services.AddDefaultJsonConfiguration();
        builder.Services.AddKnownIssues(
            knownIssuesCreationConfig,
            knownIssuesAnalysisLimitsConfig,
            knownIssuesKustoConfig,
            gitHubIssuesConfig);

        return builder.Services;
    }
}
