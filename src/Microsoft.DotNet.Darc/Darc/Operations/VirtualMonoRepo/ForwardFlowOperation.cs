// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ForwardFlowOperation(ForwardFlowCommandLineOptions options)
    : CodeFlowOperation(options)
{
    private readonly ForwardFlowCommandLineOptions _options = options;

    protected override async Task<bool> FlowAsync(
        string mappingName,
        NativePath targetDirectory,
        string? shaToFlow,
        CancellationToken cancellationToken)
        => await Provider.GetRequiredService<IVmrForwardFlower>()
            .FlowForwardAsync(
                mappingName,
                new NativePath(targetDirectory),
                shaToFlow,
                _options.Build,
                _options.BranchName,
                _options.BranchName,
                _options.DiscardPatches,
                cancellationToken);
}
