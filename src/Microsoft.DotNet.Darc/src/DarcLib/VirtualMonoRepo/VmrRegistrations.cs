// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public static class VmrRegistrations
{
    public static IServiceCollection AddVmrManager(this IServiceCollection services, string gitLocation)
    {
        services.TryAddTransient<IProcessManager>(s => ActivatorUtilities.CreateInstance<ProcessManager>(s, gitLocation));
        services.TryAddSingleton<ISourceMappingParser, SourceMappingParser>();
        services.TryAddSingleton<IVmrManagerFactory, VmrManagerFactory>();
        services.TryAddSingleton<IVmrManagerFactory, VmrManagerFactory>();
        services.TryAddSingleton<IRemoteFactory>(_ => new RemoteFactory());
        return services;
    }
}
