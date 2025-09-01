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
    /// <param name="skipMeaninglessUpdates">Skip creating PR if only insignificant changes are present</param>
    Task<CodeFlowResult> FlowForwardAsync(
        Subscription subscription,
        Build build,
        string headBranch,
        bool skipMeaninglessUpdates,
        CancellationToken cancellationToken = default);
}

internal class PcsVmrForwardFlower : VmrForwardFlower, IPcsVmrForwardFlower
{
    private readonly IRepositoryCloneManager _repositoryCloneManager;

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
        ICodeflowChangeAnalyzer codeflowChangeAnalyzer,
        IForwardFlowConflictResolver conflictResolver,
        IWorkBranchFactory workBranchFactory,
        IProcessManager processManager,
        IBasicBarClient barClient,
        IFileSystem fileSystem,
        ICommentCollector commentCollector,
        ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, vmrUpdater, dependencyTracker, vmrCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, codeflowChangeAnalyzer, conflictResolver, workBranchFactory, processManager, barClient, fileSystem, commentCollector, logger)
    {
        _repositoryCloneManager = repositoryCloneManager;
    }

    public async Task<CodeFlowResult> FlowForwardAsync(
        Subscription subscription,
        Build build,
        string headBranch,
        bool skipMeaninglessUpdates,
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
            skipMeaninglessUpdates,
            cancellationToken);
    }

    // During forward flow, we're targeting a specific remote VMR branch, so we should make sure our local branch is reset to it
    protected override bool ShouldResetVmr => true;
}
