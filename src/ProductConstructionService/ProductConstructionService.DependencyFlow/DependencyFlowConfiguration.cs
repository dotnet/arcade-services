// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.DependencyFlow.WorkItemProcessors;
using ProductConstructionService.DependencyFlow.WorkItems;
using Maestro.WorkItems;

namespace ProductConstructionService.DependencyFlow;

public static class DependencyFlowConfiguration
{
    public static void AddDependencyFlowProcessors(this IHostApplicationBuilder builder)
        => builder.Services.AddDependencyFlowProcessors();

    public static void AddDependencyFlowProcessors(this IServiceCollection services)
    {
        services.TryAddTransient<IPullRequestUpdaterFactory, PullRequestUpdaterFactory>();
        services.TryAddTransient<IMergePolicyEvaluator, MergePolicyEvaluator>();
        services.TryAddTransient<IPullRequestBuilder, PullRequestBuilder>();
        services.TryAddTransient<IPullRequestCommenter, PullRequestCommenter>();
        services.TryAddScoped<ISqlBarClient, SqlBarClient>();
        services.TryAddScoped<IBasicBarClient, SqlBarClient>();
        services.TryAddTransient<IPcsVmrBackFlower, PcsVmrBackFlower>();
        services.TryAddTransient<IPcsVmrForwardFlower, PcsVmrForwardFlower>();
        services.TryAddScoped<ICommentCollector, CommentCollector>();
        services.TryAddTransient<IPullRequestCommentBuilder, PullRequestCommentBuilder>();
        services.TryAddTransient<ISubscriptionEventRecorder, SubscriptionEventRecorder>();
        services.TryAddScoped<ISubscriptionUpdateOutcomeRecorder, SubscriptionUpdateOutcomeRecorder>();
        services.TryAddScoped<IServiceCommitTracker, ServiceCommitTracker>();
        services.AddTransient<ILocalGitClient, TrackingLocalGitClient>();
        services.AddTransient<ILocalLibGit2Client, TrackingLocalLibGit2Client>();

        services.AddWorkItemProcessor<BuildCoherencyInfoWorkItem, BuildCoherencyInfoProcessor>();
        services.AddWorkItemProcessor<PullRequestCheck, PullRequestCheckProcessor>();
        services.AddWorkItemProcessor<SubscriptionTriggerWorkItem, SubscriptionTriggerProcessor>();
        services.AddWorkItemProcessor<SubscriptionUpdateWorkItem, SubscriptionUpdateProcessor>();
        services.AddWorkItemProcessor<BackflowStatusCalculationWorkItem, BackflowStatusCalculationProcessor>();
        services.AddWorkItemProcessor<CodeflowApprovalCheck, CodeflowApprovalCheckProcessor>();
    }

    /// <summary>
    /// Registers the service that approves codeflow pull requests using a dedicated GitHub App
    /// (separate from the main PCS GitHub App) that has permission to approve pull requests.
    /// </summary>
    public static void AddCodeflowPullRequestApprover(this IServiceCollection services, string? gitHubAppId, string? gitHubAppPrivateKey)
    {
        services.Configure<GitHubTokenProviderOptions>(GitHubPullRequestApprover.GitHubAppOptionsName, o =>
        {
            o.GitHubAppId = !string.IsNullOrEmpty(gitHubAppId) ? int.Parse(gitHubAppId) : 0;
            o.PrivateKey = gitHubAppPrivateKey;
        });
        services.TryAddSingleton<IPullRequestApprover, GitHubPullRequestApprover>();
    }
}
