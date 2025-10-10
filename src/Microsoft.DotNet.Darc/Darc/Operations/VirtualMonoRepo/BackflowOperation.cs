// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class BackflowOperation(
    BackflowCommandLineOptions options,
    IVmrInfo vmrInfo,
    IVmrBackFlower backFlower,
    IBackflowConflictResolver backflowConflictResolver,
    IVmrCloneManager vmrCloneManager,
    IVmrDependencyTracker dependencyTracker,
    ILocalGitRepoFactory localGitRepoFactory,
    IDependencyFileManager dependencyFileManager,
    IBasicBarClient barApiClient,
    IProcessManager processManager,
    IFileSystem fileSystem,
    ILogger<BackflowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, vmrCloneManager, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly BackflowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IVmrBackFlower _backFlower = backFlower;
    private readonly IBackflowConflictResolver _backflowConflictResolver = backflowConflictResolver;
    private readonly IProcessManager _processManager = processManager;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(targetDirectory))
        {
            throw new DarcException("Please specify path to a local repository to flow to");
        }

        _vmrInfo.VmrPath = new NativePath(_options.VmrPath ?? _processManager.FindGitRoot(Environment.CurrentDirectory));
        var targetRepoPath = new NativePath(_processManager.FindGitRoot(targetDirectory));

        await FlowCodeLocallyAsync(
            targetRepoPath,
            isForwardFlow: false,
            additionalRemotes,
            cancellationToken);
    }

    protected override async Task<bool> FlowCodeAsync(
        ILocalGitRepo productRepo,
        Build build,
        Codeflow currentFlow,
        SourceMapping mapping,
        string headBranch,
        CancellationToken cancellationToken)
    {
        LastFlows lastFlows = await _backFlower.GetLastFlowsAsync(
            mapping.Name,
            productRepo,
            currentIsBackflow: true);

        try
        {
            var result = await _backFlower.FlowBackAsync(
                mapping.Name,
                productRepo.Path,
                build,
                excludedAssets: [], // TODO: Fill from subscription
                headBranch,
                headBranch,
                enableRebase: true,
                forceUpdate: true,
                cancellationToken);

            return result.HadUpdates;
        }
        finally
        {
            await _backflowConflictResolver.TryMergingBranchAndUpdateDependencies(
                mapping,
                lastFlows,
                (Backflow)currentFlow,
                productRepo,
                build,
                headBranch,
                headBranch,
                [],
                headBranchExisted: true,
                enableRebase: true,
                cancellationToken);
        }
    }
}
