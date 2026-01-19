// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.DependencyFlow.WorkItemProcessors;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow;

public static class DependencyFlowConfiguration
{
    public static void AddDependencyFlowProcessors(this IHostApplicationBuilder builder)
        => builder.Services.AddDependencyFlowProcessors();

    public static void AddDependencyFlowProcessors(this IServiceCollection services)
    {
        services.TryAddTransient<IPullRequestUpdaterFactory, PullRequestUpdaterFactory>();
        services.TryAddSingleton<IMergePolicyEvaluator, MergePolicyEvaluator>();
        services.TryAddTransient<IPullRequestBuilder, PullRequestBuilder>();
        services.TryAddTransient<IPullRequestCommenter, PullRequestCommenter>();
        services.TryAddScoped<ISqlBarClient, SqlBarClient>();
        services.TryAddScoped<IBasicBarClient, SqlBarClient>();
        services.TryAddTransient<IPcsVmrBackFlower, PcsVmrBackFlower>();
        services.TryAddTransient<IPcsVmrForwardFlower, PcsVmrForwardFlower>();
        services.TryAddScoped<ICommentCollector, CommentCollector>();
        services.TryAddTransient<IPullRequestCommentBuilder, PullRequestCommentBuilder>();

        services.AddWorkItemProcessor<BuildCoherencyInfoWorkItem, BuildCoherencyInfoProcessor>();
        services.AddWorkItemProcessor<PullRequestCheck, PullRequestCheckProcessor>();
        services.AddWorkItemProcessor<SubscriptionTriggerWorkItem, SubscriptionTriggerProcessor>();
        services.AddWorkItemProcessor<SubscriptionUpdateWorkItem, SubscriptionUpdateProcessor>();
        services.AddWorkItemProcessor<BackflowStatusCalculationWorkItem, BackflowStatusCalculationProcessor>();
    }
}
