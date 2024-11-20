// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class BackflowOperation(
    BackflowCommandLineOptions options,
    IVmrBackFlower vmrBackFlower,
    IVmrInfo vmrInfo,
    IVmrDependencyTracker dependencyTracker,
    ILocalGitRepoFactory localGitRepoFactory,
    ILogger<BackflowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, dependencyTracker, localGitRepoFactory, logger)
{
    private readonly BackflowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;

    protected override async Task<bool> FlowAsync(
        string mappingName,
        NativePath targetDirectory,
        string? targetRepoPath,
        CancellationToken cancellationToken)
    {
        targetRepoPath ??= Environment.CurrentDirectory!;
        var targetRepo = new NativePath(targetRepoPath);
        return await vmrBackFlower.FlowBackAsync(
            mappingName,
            targetRepo,
            shaToFlow: null,
            _options.Build,
            excludedAssets: null,
            await GetBaseBranch(targetRepo),
            await GetTargetBranch(_vmrInfo.VmrPath),
            _options.DiscardPatches,
            cancellationToken);
    }
}
