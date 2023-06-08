// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrRepoVersionResolver
{
    /// <summary>
    /// Returns a SHA of the commit that the mapped repo is currently at in the VMR.
    /// </summary>
    /// <param name="mappingName">Name of a repository mapping</param>
    Task<string> GetVersion(string mappingName);
}

internal class VmrRepoVersionResolver : IVmrRepoVersionResolver
{
    private readonly IVmrDependencyTracker _dependencyTracker;

    public VmrRepoVersionResolver(IVmrDependencyTracker dependencyTracker)
    {
        _dependencyTracker = dependencyTracker;
    }

    public async Task<string> GetVersion(string mappingName)
    {
        await _dependencyTracker.InitializeSourceMappings();

        SourceMapping mapping = _dependencyTracker.Mappings.FirstOrDefault(m => m.Name == mappingName)
            ?? throw new ArgumentException($"No mapping named {mappingName} found");

        return _dependencyTracker.GetDependencyVersion(mapping)?.Sha
            ?? throw new Exception($"Mapping {mappingName} has not been initialized yet");
    }
}
