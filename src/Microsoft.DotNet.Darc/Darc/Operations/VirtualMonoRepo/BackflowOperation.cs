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
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class BackflowOperation(
    BackflowCommandLineOptions options,
    IVmrBackFlower vmrBackFlower,
    IVmrInfo vmrInfo,
    IVmrDependencyTracker dependencyTracker,
    ILocalGitRepoFactory localGitRepoFactory,
    IBasicBarClient basicBarClient,
    ILogger<BackflowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, dependencyTracker, localGitRepoFactory, logger)
{
    private readonly BackflowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;

    protected override async Task<CodeFlowResult> FlowAsync(
        string mappingName,
        NativePath targetDirectory,
        CancellationToken cancellationToken)
    {
        var build = await basicBarClient.GetBuildAsync(_options.Build
            ?? throw new ArgumentException("Please specify a build to flow"));

        return await vmrBackFlower.FlowBackAsync(
            mappingName,
            targetDirectory,
            build,
            excludedAssets: null,
            await GetBaseBranch(targetDirectory),
            await GetTargetBranch(_vmrInfo.VmrPath),
            _options.DiscardPatches,
            cancellationToken);
    }
}
