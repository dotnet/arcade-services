// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BuildInsights.BuildAnalysis.Services;

namespace BuildInsights.BuildAnalysis;

public static class BuildDataProviderExtension
{
    public static IServiceCollection AddAzureDevOpsBuildData(this IServiceCollection services, Action<AzureDevOpsSettingsCollection> configure = null)
    {
        services.AddVssConnection(configure);
        services.TryAddSingleton<IBuildDataService, BuildDataProvider>();
        

        return services;
    }
}
