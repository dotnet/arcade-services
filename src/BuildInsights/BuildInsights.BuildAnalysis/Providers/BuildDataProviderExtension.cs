// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BuildInsights.BuildAnalysis.Services;
using Microsoft.Internal.Helix.Utility.AzureDevOps.Models;
using Microsoft.Internal.Helix.Utility.AzureDevOps.Providers;

namespace BuildInsights.BuildAnalysis.Providers;

public static class BuildDataProviderExtension
{
    public static IServiceCollection AddAzureDevOpsBuildData(this IServiceCollection services, Action<AzureDevOpsSettingsCollection> configure = null)
    {
        services.AddVssConnection(configure);
        services.TryAddSingleton<IBuildDataService, BuildDataProvider>();
        

        return services;
    }
}
