// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
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
        string tmpPath,
        string? gitHubToken,
        string? azureDevOpsToken)
    {
        RegisterManagers(services, gitLocation);
        services.TryAddSingleton<IVmrInfo>(new VmrInfo(Path.GetFullPath(vmrPath), Path.GetFullPath(tmpPath)));
        services.TryAddSingleton(new VmrRemoteConfiguration(gitHubToken, azureDevOpsToken));
        return services;
    }

    public static IServiceCollection AddVmrManagers(
        this IServiceCollection services,
        string gitLocation,
        string vmrPath,
        string tmpPath,
        Func<IServiceProvider, VmrRemoteConfiguration> configure)
    {
        RegisterManagers(services, gitLocation);
        services.TryAddSingleton(configure);
        services.TryAddSingleton<IVmrInfo>(new VmrInfo(vmrPath, tmpPath));
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
        services.TryAddTransient<IVmrDependencyTracker, VmrDependencyTracker>();
        services.TryAddTransient<IThirdPartyNoticesGenerator, ThirdPartyNoticesGenerator>();
        services.TryAddTransient<IReadmeComponentListGenerator, ReadmeComponentListGenerator>();
        services.TryAddSingleton<IRepositoryCloneManager, RepositoryCloneManager>();
        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton<IGitRepoClonerFactory, GitRepoClonerFactory>();

        // These initialize the configuration by reading the JSON files in VMR's src/
        services.TryAddSingleton<IReadOnlyCollection<SourceMapping>>(sp =>
        {
            var configuration = sp.GetRequiredService<IVmrInfo>();
            var mappingParser = sp.GetRequiredService<ISourceMappingParser>();
            return mappingParser.ParseMappings().GetAwaiter().GetResult();
        });
        services.TryAddSingleton<ISourceManifest>(sp =>
        {
            var configuration = sp.GetRequiredService<IVmrInfo>();
            return SourceManifest.FromJson(configuration.GetSourceManifestPath());
        });
    }
}
