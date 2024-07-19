// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ForwardFlowOperation(
    CommandLineOptions options,
    IVmrForwardFlower vmrForwardFlower,
    IVmrInfo vmrInfo,
    ILogger<ForwardFlowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, logger)
{
    private readonly ForwardFlowCommandLineOptions _options = (ForwardFlowCommandLineOptions)options;

    protected override async Task<bool> FlowAsync(
        string mappingName,
        NativePath targetDirectory,
        string? shaToFlow,
        CancellationToken cancellationToken)
        => await vmrForwardFlower.FlowForwardAsync(
                mappingName,
                new NativePath(targetDirectory),
                shaToFlow,
                _options.Build,
                _options.BranchName,
                _options.BranchName,
                _options.DiscardPatches,
                cancellationToken);
}
