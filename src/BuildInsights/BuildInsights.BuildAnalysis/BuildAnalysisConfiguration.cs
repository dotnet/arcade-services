// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.AzureStorage.Cache;
using BuildInsights.BuildAnalysis.HandleBar;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.WorkItems.Models;
using BuildInsights.BuildAnalysis.WorkItems.Processors;
using BuildInsights.GitHub;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using BuildInsights.QueueInsights;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductConstructionService.WorkItems;

namespace BuildInsights.BuildAnalysis;

public static class BuildAnalysisConfiguration
{
    public static IServiceCollection AddBuildAnalysis(
        this IServiceCollection services,
        IConfigurationSection knownIssuesCreationConfig,
        IConfigurationSection knownIssuesAnalysisLimitsConfig,
        IConfigurationSection kustoConfig,
        IConfigurationSection blobStorageConfig,
        IConfigurationSection queueInsightsBetaConfig,
        IConfigurationSection matrixOfTruthConfig,
        IConfigurationSection internalProjectConfig,
        IConfigurationSection buildConfigurationFileConfig,
        IConfigurationSection gitHubIssuesConfig,
        IConfigurationSection relatedBuildsConfig,
        IConfigurationSection buildAnalysisFileConfig)
    {
        services.TryAddSingleton<IMarkdownGenerator, MarkdownGenerator>();
        services.AddHandleBarHelpers();
        services.AddBlobStorageCaching(blobStorageConfig);
        services.AddKnownIssues(knownIssuesCreationConfig, knownIssuesAnalysisLimitsConfig, kustoConfig, gitHubIssuesConfig);
        services.AddQueueInsights(queueInsightsBetaConfig, matrixOfTruthConfig);
        services.AddKustoClientProvider(kustoConfig.Key);

        services.TryAddScoped<IGitHubChecksService, GitHubChecksProvider>();
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

        services.Configure<InternalProjectSettings>(internalProjectConfig);
        services.Configure<BuildConfigurationFileSettings>(buildConfigurationFileConfig);
        services.Configure<RelatedBuildProviderSettings>(relatedBuildsConfig);
        services.Configure<BuildAnalysisFileSettings>(buildAnalysisFileConfig);

        services.AddWorkItemProcessor<BuildAnalysisRequestWorkItem, BuildAnalysisProcessor>();
        services.AddWorkItemProcessor<CheckRunConclusionUpdateEvent, CheckRunConclusionUpdateProcessor>();
        services.AddWorkItemProcessor<CheckRunRerunGitHubEvent, CheckRunRerunEventProcessor>();
        services.AddWorkItemProcessor<KnownIssueAnalysisRequest, KnownIssuesAnalysisRequestProcessor>();
        services.AddWorkItemProcessor<KnownIssueValidationRequest, KnownIssueValidationProcessor>();
        services.AddWorkItemProcessor<PullRequestGitHubEventWorkItem, PullRequestEventProcessor>();

        return services;
    }
}
