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
    /// <param name="enableRebase">Rebases changes (and leaves conflict markers in place) instead of recreating the previous flows recursively</param>
    /// <param name="forceUpdate">Force the update to be performed</param>
    Task<CodeFlowResult> FlowForwardAsync(
        Subscription subscription,
        Build build,
        string headBranch,
        bool enableRebase,
        bool forceUpdate,
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
        _vmrInfo = vmrInfo;
        _repositoryCloneManager = repositoryCloneManager;
        _localGitRepoFactory = localGitRepoFactory;
    }

    public async Task<CodeFlowResult> FlowForwardAsync(
        Subscription subscription,
        Build build,
        string headBranch,
        bool enableRebase,
        bool forceUpdate,
        CancellationToken cancellationToken = default)
    {
        ILocalGitRepo sourceRepo = await _repositoryCloneManager.PrepareCloneAsync(
            build.GetRepository(),
            build.Commit,
            ShouldResetClones,
            cancellationToken);

        CodeFlowResult result = await FlowForwardAsync(
            subscription.TargetDirectory,
            sourceRepo.Path,
            build,
            subscription.ExcludedAssets,
            subscription.TargetBranch,
            headBranch,
            subscription.TargetRepository,
            enableRebase,
            forceUpdate,
            unsafeFlow: false,
            cancellationToken);

        if (enableRebase && result.HadUpdates && !result.HadConflicts)
        {
            var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
            var stagedFiles = await vmr.GetStagedFilesAsync();
            if (stagedFiles.Count > 0)
            {
                // When we do a rebase flow, the files stay staged and we need to commit them
                await vmr.CommitAsync("Update dependencies", allowEmpty: false, cancellationToken: cancellationToken);
            }
        }

        return result;
    }

    // During forward flow, we're targeting a specific remote VMR branch, so we should make sure our local branch is reset to it
    protected override bool ShouldResetVmr => true;

    protected override async Task<IReadOnlyCollection<UnixPath>> MergeWorkBranchAsync(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo targetRepo,
        IWorkBranch workBranch,
        bool headBranchExisted,
        string commitMessage,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<UnixPath> conflicts = await base.MergeWorkBranchAsync(
            codeflowOptions,
            targetRepo,
            workBranch,
            headBranchExisted,
            commitMessage,
            cancellationToken);

        if (codeflowOptions.EnableRebase && conflicts.Count == 0)
        {
            // When we do the rebase flow, we need only stage locally (in darc) after we rebase the work branch
            // In the service, we need to commit too so that we push the update to the PR
            await targetRepo.CommitAsync(commitMessage, allowEmpty: true, cancellationToken: cancellationToken);
        }

        return conflicts;
    }
}
