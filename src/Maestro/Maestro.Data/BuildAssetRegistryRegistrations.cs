// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maestro.Data;

public static class BuildAssetRegistryRegistrations
{
    public static IServiceCollection AddBuildAssetRegistry(this IServiceCollection services, Action<DbContextOptionsBuilder> optionsAction = null)
    {
        services.AddDbContext<BuildAssetRegistryContext>(optionsAction);
        services.AddSingleton<IInstallationLookup, BuildAssetRegistryInstallationLookup>();
        return services;
    }
}
