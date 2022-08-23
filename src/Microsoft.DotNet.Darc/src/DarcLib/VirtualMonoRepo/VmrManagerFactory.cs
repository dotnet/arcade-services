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
    Task<IVmrInitializer> CreateVmrInitializer();
    Task<IVmrInitializer> CreateVmrInitializer(string vmrPath, string tmpPath);
    Task<IVmrUpdater> CreateVmrUpdater();
    Task<IVmrUpdater> CreateVmrUpdater(string vmrPath, string tmpPath);
}

public class VmrManagerFactory : IVmrManagerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISourceMappingParser _sourceMappingParser;
    private readonly IVmrManagerConfiguration _configuration;

    public VmrManagerFactory(
        IServiceProvider serviceProvider,
        ISourceMappingParser sourceMappingParser,
        IVmrManagerConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _sourceMappingParser = sourceMappingParser;
        _configuration = configuration;
    }

    public Task<IVmrInitializer> CreateVmrInitializer()
        => CreateVmrManager<IVmrInitializer, VmrInitializer>();

    public Task<IVmrInitializer> CreateVmrInitializer(string vmrPath, string tmpPath)
        => CreateVmrManager<IVmrInitializer, VmrInitializer>(vmrPath, tmpPath);

    public Task<IVmrUpdater> CreateVmrUpdater()
        => CreateVmrManager<IVmrUpdater, VmrUpdater>();

    public Task<IVmrUpdater> CreateVmrUpdater(string vmrPath, string tmpPath)
        => CreateVmrManager<IVmrUpdater, VmrUpdater>(vmrPath, tmpPath);

    private async Task<R> CreateVmrManager<R, T>() where T : R
    {
        var mappings = await _sourceMappingParser.ParseMappings(_configuration.VmrPath);
        return ActivatorUtilities.CreateInstance<T>(_serviceProvider, mappings);
    }

    private async Task<R> CreateVmrManager<R, T>(string vmrPath, string tmpPath) where T : R
    {
        var mappings = await _sourceMappingParser.ParseMappings(_configuration.VmrPath);
        return ActivatorUtilities.CreateInstance<T>(_serviceProvider, new VmrManagerConfiguration(vmrPath, tmpPath), mappings);
    }
}
