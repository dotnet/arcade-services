// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, gitLocation));
        services.TryAddTransient<ISourceMappingParser, SourceMappingParser>();
        services.TryAddTransient<IVmrManagerFactory, VmrManagerFactory>();
        services.TryAddSingleton<IVmrManagerConfiguration>(new VmrManagerConfiguration(vmrPath, tmpPath));
        return services;
    }

    public static IServiceCollection AddVmrManagers(
        this IServiceCollection services,
        string gitLocation,
        Func<IServiceProvider, IVmrManagerConfiguration> configureOptions)
    {
        services.TryAddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, gitLocation));
        services.TryAddTransient<ISourceMappingParser, SourceMappingParser>();
        services.TryAddTransient<IVmrManagerFactory, VmrManagerFactory>();
        services.AddSingleton<IVmrManagerConfiguration>(configureOptions);
        return services;
    }
}
