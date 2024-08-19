// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Jobs.JobProcessors;
using ProductConstructionService.Jobs.Jobs;

namespace ProductConstructionService.Jobs;

public static class JobRegistration
{
    public static void AddJobProcessors(this IServiceCollection services)
    {
        services.RegisterJobProcessor<TextJob, TextJobProcessor>();
        services.RegisterJobProcessor<CodeFlowJob, CodeFlowJobProcessor>();
    }

    private static void RegisterJobProcessor<TJob, TProcessor>(this IServiceCollection services)
        where TProcessor : class, IJobProcessor
    {
        services.AddKeyedTransient<IJobProcessor, TProcessor>(typeof(TJob).Name);
    }
}
