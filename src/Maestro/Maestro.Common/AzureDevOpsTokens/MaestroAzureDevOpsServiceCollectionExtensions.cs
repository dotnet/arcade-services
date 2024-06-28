// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Maestro.Common.AzureDevOpsTokens;

public static class MaestroAzureDevOpsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Azure DevOps token provider.
    /// </summary>
    /// <param name="staticOptions">If provided, will initialize these options. Otherwise will try to monitor configuration.</param>
    public static IServiceCollection AddAzureDevOpsTokenProvider(
        this IServiceCollection services,
        AzureDevOpsTokenProviderOptions? staticOptions = null)
    {
        if (staticOptions != null)
        {
            services.AddSingleton(staticOptions);
        }

        return services.AddSingleton<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();
    }
}
