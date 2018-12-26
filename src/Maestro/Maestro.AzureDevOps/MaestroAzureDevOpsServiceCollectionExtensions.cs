// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;

namespace Maestro.AzureDevOps
{
    public static class MaestroAzureDevOpsServiceCollectionExtensions
    {
        public static IServiceCollection AddAzureDevOpsTokenProvider(this IServiceCollection services)
        {
            return services.AddSingleton<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();
        }
    }
}
