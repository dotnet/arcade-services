// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class GenerateTpnOperation : Operation
{
    private readonly GenerateTpnCommandLineOptions _options;
    private readonly IThirdPartyNoticesGenerator _generator;
    private readonly IVmrDependencyTracker _dependencyTracker;

    public GenerateTpnOperation(
        GenerateTpnCommandLineOptions options,
        IThirdPartyNoticesGenerator generator,
        IVmrDependencyTracker dependencyTracker)
    {
        _options = options;
        _generator = generator;
        _dependencyTracker = dependencyTracker;
    }

    public override async Task<int> ExecuteAsync()
    {
        await _dependencyTracker.RefreshMetadata();
        await _generator.UpdateThirdPartyNotices(_options.TpnTemplate);
        return 0;
    }
}
