// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrManagerFactory
{
    Task<IVmrManager> CreateVmrManager(IServiceProvider services, string vmrPath, string tmpPath);
}

public class VmrManagerFactory : IVmrManagerFactory
{
    private readonly ISourceMappingParser _sourceMappingParser;

    public VmrManagerFactory(ISourceMappingParser sourceMappingParser)
    {
        _sourceMappingParser = sourceMappingParser;
    }

    public async Task<IVmrManager> CreateVmrManager(IServiceProvider services, string vmrPath, string tmpPath)
    {
        var mappings = await _sourceMappingParser.ParseMappings(vmrPath);
        return ActivatorUtilities.CreateInstance<VmrManager>(services, mappings, vmrPath, tmpPath);
    }
}
