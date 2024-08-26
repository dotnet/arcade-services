// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
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
        services.AddTransient<IActorFactory, ActorFactory>();

        services.AddWorkItemProcessor<BuildCoherencyInfoWorkItem, BuildCoherencyInfoProcessor>();
        services.AddWorkItemProcessor<CodeFlowWorkItem, CodeFlowWorkItemProcessor>();
        services.AddWorkItemProcessor<InProgressPullRequest, PullRequestCheckProcessor>();
        services.AddWorkItemProcessor<SubscriptionTriggerWorkItem, SubscriptionTriggerProcessor>();
        services.AddWorkItemProcessor<SubscriptionUpdateWorkItem, SubscriptionUpdateProcessor>();
    }
}
