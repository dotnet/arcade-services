// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.WorkItems.WorkItemDefinitions;
using ProductConstructionService.WorkItems.WorkItemProcessors;

namespace ProductConstructionService.WorkItems;

public static class WorkItemRegistration
{
    public static void AddWorkItemProcessors(this IServiceCollection services)
    {
        services.RegisterWorkItemProcessor<CodeFlowWorkItem, CodeFlowWorkItemProcessor>();
    }

    private static void RegisterWorkItemProcessor<TWorkItem, TProcessor>(this IServiceCollection services)
        where TProcessor : class, IWorkItemProcessor
    {
        services.AddKeyedTransient<IWorkItemProcessor, TProcessor>(typeof(TWorkItem).Name);
    }
}
