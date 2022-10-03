// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public static class VmrRegistrations
{
    public static IServiceCollection AddVmrManagers(
        this IServiceCollection services,
        string gitLocation,
        string vmrPath,
        string tmpPath)
    {
        RegisterManagers(services, gitLocation);
        services.TryAddSingleton<IVmrManagerConfiguration>(new VmrManagerConfiguration(vmrPath, tmpPath));
        return services;
    }

    public static IServiceCollection AddVmrManagers(
        this IServiceCollection services,
        string gitLocation,
        Func<IServiceProvider, IVmrManagerConfiguration> configure)
    {
        RegisterManagers(services, gitLocation);
        services.TryAddSingleton<IVmrManagerConfiguration>(configure);
        return services;
    }

    private static void RegisterManagers(IServiceCollection services, string gitLocation)
    {
        services.TryAddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, gitLocation));
        services.TryAddTransient<ILocalGitRepo>(sp => ActivatorUtilities.CreateInstance<LocalGitClient>(sp, gitLocation));
        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<IVmrManager>>());
        services.TryAddTransient<ISourceMappingParser, SourceMappingParser>();
        services.TryAddTransient<IVersionDetailsParser, VersionDetailsParser>();
        services.TryAddTransient<IVmrDependencyTracker, VmrDependencyTracker>();
        services.TryAddTransient<IVmrPatchHandler, VmrPatchHandler>();
        services.TryAddTransient<IVmrUpdater, VmrUpdater>();
        services.TryAddTransient<IVmrInitializer, VmrInitializer>();
        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton<IReadOnlyCollection<SourceMapping>>(sp =>
        {
            var configuration = sp.GetRequiredService<IVmrManagerConfiguration>();
            var mappingParser = sp.GetRequiredService<ISourceMappingParser>();
            return mappingParser.ParseMappings(configuration.VmrPath).GetAwaiter().GetResult();
        });
    }
}
