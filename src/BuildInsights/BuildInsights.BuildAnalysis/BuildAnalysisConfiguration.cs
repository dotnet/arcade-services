// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.AzureStorage.Cache;
using BuildInsights.BuildAnalysis.HandleBar;
using BuildInsights.GitHub;
using BuildInsights.KnownIssues;
using BuildInsights.QueueInsights;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.BuildAnalysis;

public static class BuildAnalysisConfiguration
{
    public static IServiceCollection AddBuildAnalysis(
        this IServiceCollection services,
        IConfigurationSection knownIssuesCreationConfig,
        IConfigurationSection knownIssuesAnalysisLimitsConfig,
        IConfigurationSection knownIssuesKustoConfig,
        IConfigurationSection blobStorageConfig,
        IConfigurationSection queueInsightsBetaConfig,
        IConfigurationSection matrixOfTruthConfig)
    {
        services.TryAddSingleton<IMarkdownGenerator, MarkdownGenerator>();
        services.AddHandleBarHelpers();
        services.AddBlobStorageCaching(blobStorageConfig);
        services.AddKnownIssues(knownIssuesCreationConfig, knownIssuesAnalysisLimitsConfig, knownIssuesKustoConfig);
        services.AddQueueInsights(queueInsightsBetaConfig, matrixOfTruthConfig);
        services.AddKustoClientProvider(knownIssuesKustoConfig.Key); // Same as known issues kusto config

        services.TryAddScoped<IGitHubChecksService, GitHubChecksProvider>();
        services.TryAddScoped<IGitHubIssuesService, GitHubIssuesProvider>();
        services.TryAddScoped<IRelatedBuildService, RelatedBuildProvider>();
        services.TryAddScoped<IGitHubRepositoryService, GithubRepositoryProvider>();
        services.TryAddScoped<IAzDoToGitHubRepositoryService, AzDoToGitHubRepositoryProvider>();
        services.TryAddScoped<IBuildDataService, BuildDataProvider>();
        services.TryAddScoped<IBuildAnalysisHistoryService, BuildAnalysisHistoryProvider>();
        services.TryAddScoped<IBuildAnalysisRepositoryConfigurationService, BuildAnalysisRepositoryConfigurationProvider>();
        services.TryAddScoped<IBuildAnalysisService, BuildAnalysisProvider>();
        services.TryAddScoped<IBuildCacheService, BuildCacheProvider>();
        services.TryAddScoped<IBuildOperationsService, BuildOperationsProvider>();
        services.TryAddScoped<IBuildProcessingStatusService, BuildProcessingStatusStatusProvider>();
        services.TryAddScoped<IBuildRetryService, BuildRetryProvider>();
        services.TryAddScoped<ICheckResultService, CheckResultProvider>();
        services.TryAddScoped<IHelixDataService, HelixDataProvider>();
        services.TryAddScoped<IKnownIssueValidationService, KnownIssueValidationProvider>();
        services.TryAddScoped<IKustoClientProvider, KustoClientProvider>();
        services.TryAddScoped<IKustoIngestClientFactory, KustoIngestClientFactory>();
        services.TryAddScoped<IMergedBuildAnalysisService, MergedBuildAnalysisProvider>();
        services.TryAddScoped<IPipelineRequestedService, PipelineRequestedProvider>();
        services.TryAddScoped<IPreviousBuildAnalysisService, PreviousBuildAnalysisProvider>();
        services.TryAddScoped<ITestResultService, TestResultProvider>();

        services.AddDefaultJsonConfiguration();
        services.Configure<InternalProject>("InternalProject", (o, c) => c.Bind(o));
        services.Configure<SentimentUrlOptions>("UserSentiment", (o, c) => c.Bind(o));
        services.Configure<BuildConfigurationFileSettings>("BuildConfigurationFileSettings", (o, c) => c.Bind(o));
        services.Configure<GitHubIssuesSettings>("GitHubIssuesSettings", (o, c) => c.Bind(o));
        services.Configure<RelatedBuildProviderSettings>("RelatedBuildProviderSettings", (o, c) => c.Bind(o));
        services.Configure<BuildAnalysisFileSettings>("BuildAnalysisFileSettings", (o, c) => c.Bind(o));

        return services;
    }
}
