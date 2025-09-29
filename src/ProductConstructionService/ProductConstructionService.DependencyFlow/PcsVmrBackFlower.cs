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
/// Class for flowing code from the VMR to product repos.
/// This class is used in the context of darc CLI as some behaviours around repo preparation differ.
/// </summary>
internal interface IPcsVmrBackFlower : IVmrBackFlower
{
    /// <summary>
    /// Flows backward the code from the VMR to the target branch of a product repo.
    /// This overload is used in the context of the PCS.
    /// </summary>
    /// <param name="subscription">Subscription to flow</param>
    /// <param name="build">Build to flow</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <returns>
    ///     Boolean whether there were any changes to be flown
    ///     and a path to the local repo where the new branch is created
    ///  </returns>
    Task<CodeFlowResult> FlowBackAsync(
        Subscription subscription,
        Build build,
        string targetBranch,
        bool forceApply,
        CancellationToken cancellationToken = default);
}

internal class PcsVmrBackFlower : VmrBackFlower, IPcsVmrBackFlower
{
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly IRepositoryCloneManager _repositoryCloneManager;
    private readonly ILogger<VmrCodeFlower> _logger;

    public PcsVmrBackFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            IRepositoryCloneManager repositoryCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler vmrPatchHandler,
            IWorkBranchFactory workBranchFactory,
            IBackflowConflictResolver versionFileConflictResolver,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ICommentCollector commentCollector,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, vmrCloneManager, repositoryCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, vmrPatchHandler, workBranchFactory, versionFileConflictResolver, fileSystem, barClient, commentCollector, logger)
    {
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _vmrCloneManager = vmrCloneManager;
        _repositoryCloneManager = repositoryCloneManager;
        _logger = logger;
    }

    public async Task<CodeFlowResult> FlowBackAsync(
        Subscription subscription,
        Build build,
        string headBranch,
        bool forceUpdate,
        CancellationToken cancellationToken = default)
    {
        (bool headBranchExisted, SourceMapping mapping, LastFlows lastFlows, ILocalGitRepo targetRepo) = await PrepareVmrAndRepo(
            subscription.SourceDirectory,
            build,
            subscription.TargetBranch,
            headBranch,
            targetRepoPath: null,
            cancellationToken);

        var result = await FlowBackAsync(
            mapping,
            targetRepo,
            lastFlows,
            build,
            subscription.ExcludedAssets,
            subscription.TargetBranch,
            headBranch,
            headBranchExisted,
            forceUpdate,
            cancellationToken);

        return result with
        {
            // For already existing PRs, we want to always push the changes (even if only the <Source> tag changed)
            HadUpdates = result.HadUpdates || headBranchExisted,
        };
    }

    protected override bool ShouldResetClones => true;
}
