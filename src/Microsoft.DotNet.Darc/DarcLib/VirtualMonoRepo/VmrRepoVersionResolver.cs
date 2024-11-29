// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

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
        await _dependencyTracker.RefreshMetadata();

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);

        return _dependencyTracker.GetDependencyVersion(mapping)?.Sha
            ?? throw new Exception($"Mapping {mappingName} has not been initialized yet");
    }
}
