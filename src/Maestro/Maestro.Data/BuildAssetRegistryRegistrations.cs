// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maestro.Data
{
    public static class BuildAssetRegistryRegistrations
    {
        public static IServiceCollection AddBuildAssetRegistry(this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> optionsAction = null)
        {
            services.AddDbContext<BuildAssetRegistryContext>(optionsAction);
            services.AddScoped<IInstallationLookup>(c => c.GetRequiredService<BuildAssetRegistryContext>());
            return services;
        }

        public static IServiceCollection AddBuildAssetRegistry(this IServiceCollection services, Action<DbContextOptionsBuilder> optionsAction = null)
        {
            services.AddDbContext<BuildAssetRegistryContext>(optionsAction);
            services.AddScoped<IInstallationLookup>(c => c.GetRequiredService<BuildAssetRegistryContext>());
            return services;
        }
    }
}