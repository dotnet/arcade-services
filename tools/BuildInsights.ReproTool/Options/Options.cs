// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octokit;
using BuildInsights.ReproTool.Operations;
using GitHubClient = Octokit.GitHubClient;

namespace BuildInsights.ReproTool.Options;

internal abstract class Options
{
    internal const string DefaultBuildInsightsLocalUri = "https://localhost:53180";

    public string? GitHubToken { get; set; }

    public string? AzDoHookSecret { get; set; }

    [Option("build-insights-url", HelpText = "Base URL of the locally running Build Insights API.", Required = false)]
    public string? LocalBuildInsightsUrl { get; set; } = DefaultBuildInsightsLocalUri;

    internal abstract Operation GetOperation(IServiceProvider sp);

    public virtual IServiceCollection RegisterServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder
            .AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            })
            .SetMinimumLevel(LogLevel.Information));

        services.AddSingleton(_ => new GitHubClient(new ProductHeaderValue("buildinsights-repro-tool"))
        {
            Credentials = new Credentials(GitHubToken)
        });

        services.AddSingleton(_ =>
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri((LocalBuildInsightsUrl ?? DefaultBuildInsightsLocalUri).TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromMinutes(5)
            };

            client.DefaultRequestHeaders.Add("X-BuildAnalysis-Secret", AzDoHookSecret);
            return client;
        });

        return services;
    }
}
