// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using BuildInsights.AzureStorage.Cache;
using BuildInsights.BuildAnalysis;
using BuildInsights.BuildAnalysis.WorkItems.Processors;
using BuildInsights.GitHub;
using BuildInsights.KnownIssues;
using BuildInsights.QueueInsights;
using BuildInsights.Utilities.AzureDevOps.Models;
using Microsoft.ApplicationInsights.Extensibility.EventCounterCollector;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace BuildInsights.BuildResultProcessor;

public class Program
{
    public static void Main(string[] args)
    {
        ServiceHost.Run(host =>
        {
            host.RegisterStatelessService<BuildResultProcessor>("BuildResultAnalysisProcessorType");
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
        services.Configure<KnownIssueValidationTableConnectionSettings>("KnownIssuesValidationTable", (o, c) => c.Bind(o));
        services.Configure<BlobStorageSettings>("BuildResultAnalysisBlobStorage", (o, c) => c.Bind(o));
        services.Configure<InternalProject>("InternalProject", (o, c) => c.Bind(o));
        services.Configure<ParallelismSettings>("Parallelism", (o, c) => c.Bind(o));
        services.Configure<QueueProcessingThreadSettings>("QueueProcessing", (o, c) => c.Bind(o));
        services.Configure<GitHubClientOptions>("GitHubClient", (o, c) => c.Bind(o));
        services.Configure<GitHubClientOptions>(
            options => { options.ProductHeader = new Octokit.ProductHeaderValue(assemblyName, assemblyVersion); }
        );
        services.Configure<GitHubTokenProviderOptions>("GitHubAppAuth", (o, s) => s.Bind(o));
        services.Configure<AzureDevOpsSettingsCollection>("AzureDevOps", (o, c) => c.Bind(o));
        services.Configure<SentimentUrlOptions>("UserSentiment", (o, c) => c.Bind(o));
        services.Configure<BuildConfigurationFileSettings>("BuildConfigurationFileSettings", (o, c) => c.Bind(o));
        services.Configure<GitHubIssuesSettings>("GitHubIssuesSettings", (o, c) => c.Bind(o));
        services.Configure<KnownIssueUrlOptions>("KnownIssueUrlOptions", (o, c) => c.Bind(o));
        services.Configure<KnownIssuesAnalysisLimits>("KnownIssuesAnalysisLimits", (o, c) => c.Bind(o));
        services.Configure<KustoOptions>("KnownIssuesKustoOptions", (o, c) => c.Bind(o));
        services.Configure<SqlConnectionSettings>("Sql", (o, c) => c.Bind(o));
        services.Configure<QueueInsightsBetaSettings>("QueueInsightsBeta", (o, c) => c.Bind(o));
        services.Configure<MatrixOfTruthSettings>("MatrixOfTruth", (o, c) => c.Bind(o));
        services.Configure<RelatedBuildProviderSettings>("RelatedBuildProviderSettings", (o, c) => c.Bind(o));
        services.Configure<BuildAnalysisFileSettings>("BuildAnalysisFileSettings", (o, c ) => c.Bind(o));
        services.Configure<HttpClientFactoryOptions>(options =>
        {
            options.HttpClientActions.Add(client =>
            {
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(assemblyName, assemblyVersion));
            });
        });
        services.AddKustoClientProvider("KnownIssuesKustoOptions");
        services.ConfigureTelemetryModule<EventCounterCollectionModule>((module, o) =>
        {
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "active-hard-connections"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "hard-connects"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "hard-disconnects"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "active-soft-connects"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "soft-connects"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "soft-disconnects"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "number-of-non-pooled-connections"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "number-of-pooled-connections"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "number-of-active-connection-pool-groups"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "number-of-inactive-connection-pool-groups"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "number-of-active-connection-pools"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "number-of-inactive-connection-pools"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "number-of-active-connections"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "number-of-free-connections"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "number-of-stasis-connections"));
            module.Counters.Add(new EventCounterCollectionRequest("Microsoft.Data.SqlClient.EventSource", "number-of-reclaimed-connections"));
        });
    }

    public static void AddServices(IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddAzureStorageQueue();
        services.AddQueueProcessing<AnalysisProcessor>(
            queueOptions => { },
            parallelismOptions => { parallelismOptions.WorkerCount = Environment.ProcessorCount; }
        );

        services.AddMarkdownGenerator();

        services.TryAddSingleton<IGitHubChecksService, GitHubChecksProvider>();
        services.TryAddSingleton<IGitHubIssuesService, GitHubIssuesProvider>();
        services.TryAddSingleton<IRelatedBuildService, RelatedBuildProvider>();
        services.TryAddSingleton<IGitHubRepositoryService, GithubRepositoryProvider>();

        services.TryAddScoped<IBuildAnalysisService, BuildAnalysisProvider>();
        services.TryAddScoped<IMergedBuildAnalysisService, MergedBuildAnalysisProvider>();
        services.TryAddScoped<IPreviousBuildAnalysisService, PreviousBuildAnalysisProvider>();
        services.TryAddScoped<IBuildCacheService, BuildCacheProvider>();
        services.TryAddScoped<IKnownIssueValidationService, KnownIssueValidationProvider>();
        services.TryAddScoped<IBuildAnalysisRepositoryConfigurationService, BuildAnalysisRepositoryConfigurationProvider>();

        services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        services.AddSingleton<IGitHubApplicationClientFactory, GitHubApplicationClientFactory>();
        services.AddSingleton<IInstallationLookup, InMemoryCacheInstallationLookup>();
        services.AddGitHubTokenProvider();
        services.AddSingleton(_ => TimeProvider.System);

        services.TryAddScoped<IContextualStorage, BlobContextualStorage>();
        services.TryAddScoped<IDistributedLockService, BlobContextualStorage>();
        services.TryAddSingleton<IBlobClientFactory, BlobClientFactory>();
        services.TryAddSingleton<IBuildOperationsService, BuildOperationsProvider>();
        services.TryAddSingleton<IBuildRetryService, BuildRetryProvider>();

        services.TryAddSingleton<IKustoIngestClientFactory, KustoIngestClientFactory>();
        services.TryAddSingleton<IKnownIssuesService, KnownIssuesProvider>();
        services.TryAddSingleton<IKnownIssuesHistoryService, KnownIssuesHistoryProvider>();
        services.TryAddSingleton<IKnownIssuesMatchService, KnownIssuesMatchProvider>();
        services.TryAddSingleton<IBuildProcessingStatusService, BuildProcessingStatusStatusProvider>();
        services.TryAddSingleton<IKustoClientProvider, KustoClientProvider>();
        services.TryAddSingleton<IBuildAnalysisHistoryService, BuildAnalysisHistoryProvider>();
        services.TryAddSingleton<ICheckResultService, CheckResultProvider>();
        services.TryAddSingleton<ITestResultService, TestResultProvider>();
        services.AddSingleton<IHelixDataService, HelixDataProvider>();
        services.AddSingleton<IPullRequestService,PullRequestEventProcessor>();
        services.AddScoped<IAzDoToGitHubRepositoryService, AzDoToGitHubRepositoryProvider>();
        services.TryAddSingleton<IPipelineRequestedService, PipelineRequestedProvider>();

        services.AddAzureDevOpsBuildData();

        services.AddSingleton<IQueueInsightsMarkdownGenerator, QueueInsightsMarkdownGenerator>();
        services.AddSingleton<IQueueInsightsService, QueueInsightsService>();
        services.AddSingleton<IQueueTimeService, QueueTimeService>();
        services.AddSingleton<IMatrixOfTruthService, MatrixOfTruthService>();
    }
}
