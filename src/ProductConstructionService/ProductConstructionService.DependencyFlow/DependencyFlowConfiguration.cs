﻿// Licensed to the .NET Foundation under one or more agreements.
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
    {
        builder.Services.AddTransient<IActorFactory, ActorFactory>();

        builder.Services.AddWorkItemProcessor<BuildCoherencyInfoWorkItem, BuildCoherencyInfoProcessor>();
        builder.Services.AddWorkItemProcessor<CodeFlowWorkItem, CodeFlowWorkItemProcessor>();
        builder.Services.AddWorkItemProcessor<PullRequestCheckWorkItem, PullRequestCheckProcessor>();
        builder.Services.AddWorkItemProcessor<SubscriptionTriggerWorkItem, SubscriptionTriggerProcessor>();
        builder.Services.AddWorkItemProcessor<SubscriptionUpdateWorkItem, SubscriptionUpdateProcessor>();
    }
}
