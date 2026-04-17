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
using Maestro.WorkItems;

namespace BuildInsights.BuildAnalysis;

public static class BuildAnalysisConfiguration
{
    public static IServiceCollection AddBuildAnalysis(
        this IServiceCollection services,
        IConfigurationSection knownIssuesConfig,
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

        services.TryAddScoped<IBuildAnalysisService, BuildAnalysisProvider>();
        services.TryAddScoped<IBuildCacheService, BuildCacheProvider>();
        services.TryAddScoped<IKustoClientProvider, KustoClientProvider>();
        services.TryAddScoped<IKustoIngestClientFactory, KustoIngestClientFactory>();

        services.TryAddTransient<IAzDoToGitHubRepositoryService, AzDoToGitHubRepositoryProvider>();
        services.TryAddTransient<IBuildAnalysisHistoryService, BuildAnalysisHistoryProvider>();
        services.TryAddTransient<IBuildAnalysisRepositoryConfigurationService, BuildAnalysisRepositoryConfigurationProvider>();
        services.TryAddTransient<IBuildAnalyzer, BuildAnalyzer>();
        services.TryAddTransient<IBuildDataService, BuildDataProvider>();
        services.TryAddTransient<IBuildOperationsService, BuildOperationsProvider>();
        services.TryAddTransient<IBuildProcessingStatusService, BuildProcessingStatusStatusProvider>();
        services.TryAddTransient<IBuildRetryService, BuildRetryProvider>();
        services.TryAddTransient<ICheckResultService, CheckResultProvider>();
        services.TryAddTransient<IGitHubChecksService, GitHubChecksProvider>();
        services.TryAddTransient<IGitHubRepositoryService, GithubRepositoryProvider>();
        services.TryAddTransient<IHelixDataService, HelixDataProvider>();
        services.TryAddTransient<IKnownIssueValidationService, KnownIssueValidationProvider>();
        services.TryAddTransient<IMergedBuildAnalysisService, MergedBuildAnalysisProvider>();
        services.TryAddTransient<IPipelineRequestedService, PipelineRequestedProvider>();
        services.TryAddTransient<IPreviousBuildAnalysisService, PreviousBuildAnalysisProvider>();
        services.TryAddTransient<IRelatedBuildService, RelatedBuildProvider>();
        services.TryAddTransient<ITestResultService, TestResultProvider>();

        services.Configure<KnownIssuesProcessorOptions>(knownIssuesConfig);
        services.Configure<InternalProjectSettings>(internalProjectConfig);
        services.Configure<BuildConfigurationFileSettings>(buildConfigurationFileConfig);
        services.Configure<RelatedBuildProviderSettings>(relatedBuildsConfig);
        services.Configure<BuildAnalysisFileSettings>(buildAnalysisFileConfig);

        services.AddWorkItemProcessor<BuildAnalysisRequestWorkItem, BuildAnalysisProcessor>();
        services.AddWorkItemProcessor<TestBuildAnalysisRequestWorkItem, TestBuildAnalysisProcessor>();
        services.AddWorkItemProcessor<CheckRunConclusionUpdateEvent, CheckRunConclusionUpdateProcessor>();
        services.AddWorkItemProcessor<CheckRunRerunGitHubEvent, CheckRunRerunEventProcessor>();
        services.AddWorkItemProcessor<KnownIssueAnalysisRequest, KnownIssuesAnalysisRequestProcessor>();
        services.AddWorkItemProcessor<KnownIssueValidationRequest, KnownIssueValidationProcessor>();
        services.AddWorkItemProcessor<PullRequestGitHubEventWorkItem, PullRequestEventProcessor>();

        return services;
    }
}
