// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
/// Interface for VmrForwardFlower used in the context of the PCS.
/// </summary>
internal interface IPcsVmrForwardFlower
{
    /// <summary>
    /// Flows forward the code from the source repo to the target branch of the VMR.
    /// This overload is used in the context of the PCS.
    /// </summary>
    /// <param name="subscription">Subscription to flow</param>
    /// <param name="build">Build to flow</param>
    /// <param name="headBranch">Branch to flow to (or to create)</param>
    Task<CodeFlowResult> FlowForwardAsync(
        Subscription subscription,
        Build build,
        string headBranch,
        CancellationToken cancellationToken = default);
}

internal class PcsVmrForwardFlower : VmrForwardFlower, IPcsVmrForwardFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IRepositoryCloneManager _repositoryCloneManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;

    public PcsVmrForwardFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        ICodeFlowVmrUpdater vmrUpdater,
        IVmrDependencyTracker dependencyTracker,
        IVmrCloneManager vmrCloneManager,
        IRepositoryCloneManager repositoryCloneManager,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IProcessManager processManager,
        IFileSystem fileSystem,
        IBasicBarClient barClient,
        ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, vmrUpdater, dependencyTracker, vmrCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, processManager, fileSystem, barClient, logger)
    {
        _vmrInfo = vmrInfo;
        _repositoryCloneManager = repositoryCloneManager;
        _localGitRepoFactory = localGitRepoFactory;
    }

    public async Task<CodeFlowResult> FlowForwardAsync(
        Subscription subscription,
        Build build,
        string headBranch,
        CancellationToken cancellationToken = default)
    {
        ILocalGitRepo sourceRepo = await _repositoryCloneManager.PrepareCloneAsync(
            build.GetRepository(),
            build.Commit,
            ShouldResetClones,
            cancellationToken);

        return await FlowForwardAsync(
            subscription.TargetDirectory,
            sourceRepo.Path,
            build,
            subscription.ExcludedAssets,
            subscription.TargetBranch,
            headBranch,
            subscription.TargetRepository,
            discardPatches: true,
            skipMeaninglessUpdates: true,
            cancellationToken);
    }

    // During forward flow, we're targeting a specific remote VMR branch, so we should make sure our local branch is reset to it
    protected override bool ShouldResetVmr => true;
}
