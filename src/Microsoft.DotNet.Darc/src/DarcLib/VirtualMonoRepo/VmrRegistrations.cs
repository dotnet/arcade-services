// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public static class VmrRegistrations
{
    public static IServiceCollection AddVmrManager(this IServiceCollection services) =>
        services
            .AddTransient<IProcessManager, ProcessManager>()
            .AddSingleton<ISourceMappingParser, SourceMappingParser>()
            .AddSingleton<IVmrManagerFactory, VmrManagerFactory>();
}
