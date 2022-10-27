// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo.Licenses;
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
        services.TryAddSingleton<IVmrInfo>(new VmrInfo(Path.GetFullPath(vmrPath), Path.GetFullPath(tmpPath)));
        return services;
    }

    public static IServiceCollection AddVmrManagers(
        this IServiceCollection services,
        string gitLocation,
        Func<IServiceProvider, IVmrInfo> configure)
    {
        RegisterManagers(services, gitLocation);
        services.TryAddSingleton<IVmrInfo>(configure);
        return services;
    }

    private static void RegisterManagers(IServiceCollection services, string gitLocation)
    {
        services.TryAddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, gitLocation));
        services.TryAddTransient<ILocalGitRepo>(sp => ActivatorUtilities.CreateInstance<LocalGitClient>(sp, gitLocation));
        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<IVmrManager>>());
        services.TryAddTransient<ISourceMappingParser, SourceMappingParser>();
        services.TryAddTransient<IVersionDetailsParser, VersionDetailsParser>();
        services.TryAddTransient<IVmrPatchHandler, VmrPatchHandler>();
        services.TryAddTransient<IVmrUpdater, VmrUpdater>();
        services.TryAddTransient<IVmrInitializer, VmrInitializer>();
        services.TryAddTransient<IThirdPartyNoticesGenerator, ThirdPartyNoticesGenerator>();
        services.TryAddSingleton<IRepositoryCloneManager, RepositoryCloneManager>();
        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton<IVmrDependencyTracker>(sp =>
        {
            var configuration = sp.GetRequiredService<IVmrInfo>();
            var mappingParser = sp.GetRequiredService<ISourceMappingParser>();
            var fileSystem = sp.GetRequiredService<IFileSystem>();
            var mappings = mappingParser.ParseMappings().GetAwaiter().GetResult();
            var sourceManifest = SourceManifest.FromJson(configuration.GetSourceManifestPath());
            return new VmrDependencyTracker(configuration, fileSystem, mappings, sourceManifest);
        });
    }
}
