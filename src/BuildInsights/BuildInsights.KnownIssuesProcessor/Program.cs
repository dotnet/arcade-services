// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis;
using BuildInsights.GitHub;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.KnownIssuesProcessor;

public class Program
{
    public static void Main(string[] args)
    {
        ServiceHost.Run(host =>
        {
            host.RegisterStatelessService<KnownIssuesProcessor>("KnownIssuesProcessorType");
            host.ConfigureServices(ConfigureServices);
        });
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        AddServices(services);
        SetConfiguration(services);
    }

    private static void SetConfiguration(IServiceCollection services)
    {
        (string assemblyName, string assemblyVersion) = Helpers.GetAssemblyVersion();

        services.AddDefaultJsonConfiguration();
        services.Configure<ManagedIdentity>("Secrets", (o, c) => c.Bind(o));
        services.Configure<StorageQueueConnectionSettings>("KnownIssueStorageQueue", (o, c) => c.Bind(o));
        services.Configure<ParallelismSettings>("Parallelism", (o, c) => c.Bind(o));
        services.Configure<QueueProcessingThreadSettings>("QueueProcessing", (o, c) => c.Bind(o));
        services.Configure<KnownIssuesProcessorOptions>("KnownIssuesProcessorOptions", (o, c) => c.Bind(o));
        services.Configure<AzureDevOpsSettingsCollection>("AzureDevOps", (o, c) => c.Bind(o));
        services.Configure<GitHubTokenProviderOptions>("GitHubAppAuth", (o, s) => s.Bind(o));
        services.Configure<GitHubClientOptions>("GitHubClient", (o, c) => c.Bind(o));
        services.Configure<GitHubClientOptions>(
            options => { options.ProductHeader = new Octokit.ProductHeaderValue(assemblyName, assemblyVersion); }
        );
        services.Configure<GitHubIssuesSettings>("GitHubIssuesSettings", (o, c) => c.Bind(o));
        services.AddVssConnection();
    }

    public static void AddServices(IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddAzureStorageQueue();
        services.AddQueueProcessing<KnownIssuesAnalysisRequestProcessor>(
            queueOptions => { },
            parallelismOptions => { parallelismOptions.WorkerCount = Environment.ProcessorCount; }
        );
        services.Configure<TableConnectionSettings>("KnownIssuesAnalysisTable", (o, c) => c.Bind(o));
        services.Configure<ProcessingStatusTableConnectionSettings>("ProcessingStatusTable", (o, c) => c.Bind(o));

        services.TryAddSingleton<IQueueClientFactory, QueueClientFactory>();
        services.TryAddSingleton<IKnownIssuesHistoryService, KnownIssuesHistoryProvider>();
        services.TryAddSingleton<IBuildAnalysisHistoryService, BuildAnalysisHistoryProvider>();
        services.TryAddSingleton<IRequestAnalysisService, RequestAnalysisProvider>();
        services.TryAddSingleton<IBuildProcessingStatusService, BuildProcessingStatusStatusProvider>();

        services.TryAddSingleton(_ => TimeProvider.System);
        services.TryAddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.TryAddSingleton<IGitHubApplicationClientFactory, GitHubApplicationClientFactory>();
        services.TryAddSingleton<IInstallationLookup, InMemoryCacheInstallationLookup>();
        services.AddGitHubTokenProvider();
        services.TryAddSingleton<IGitHubChecksService, GitHubChecksProvider>();
        services.TryAddSingleton<IGitHubIssuesService, GitHubIssuesProvider>();

        services.AddVssConnection();
        services.AddAzureDevOpsBuildData();
    }
}
