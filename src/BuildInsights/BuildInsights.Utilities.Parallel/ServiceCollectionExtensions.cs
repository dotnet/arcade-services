// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace BuildInsights.Utilities.Parallel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddThreadRunner<TProcessingThread>(
        this IServiceCollection services,
        Action<ParallelismSettings> configureOptions = null
    ) where TProcessingThread : class, IProcessingThread
    {
        services
            .AddSingleton<ThreadRunner>()
            .AddScoped<ProcessingThreadIdentity>()
            .AddScoped<IProcessingThread, TProcessingThread>();

        if (configureOptions != null)
        {
            services.AddOptions()
                .Configure(configureOptions);
        }

        return services;
    }

    public static void SetProcessingThreadIdentity(this IServiceScope scope, string identity)
    {
        scope.ServiceProvider.GetRequiredService<ProcessingThreadIdentity>().Initialize(identity);
    }
}
