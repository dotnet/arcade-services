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
    /// <param name="forceUpdate">Force the update to be performed</param>
    Task<CodeFlowResult> FlowBackAsync(
        Subscription subscription,
        Build build,
        string targetBranch,
        bool forceUpdate,
        CancellationToken cancellationToken = default);
}

internal class PcsVmrBackFlower : VmrBackFlower, IPcsVmrBackFlower
{
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
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, vmrCloneManager, repositoryCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, vmrPatchHandler, workBranchFactory, versionFileConflictResolver, fileSystem, barClient, logger)
    {
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
            unsafeFlow: false,
            cancellationToken);

        CodeFlowResult result = await FlowBackAsync(
            new CodeflowOptions(
                mapping,
                new Backflow(build.Commit, lastFlows.LastFlow.RepoSha),
                subscription.TargetBranch,
                headBranch,
                build,
                subscription.ExcludedAssets,
                EnableRebase: true,
                forceUpdate,
                UnsafeFlow: false),
            targetRepo,
            lastFlows,
            headBranchExisted,
            cancellationToken);

        if (result.HadUpdates && !result.HadConflicts)
        {
            var stagedFiles = await targetRepo.GetStagedFilesAsync();
            if (stagedFiles.Count > 0)
            {
                // When we do a rebase flow, the files stay staged and we need to commit them
                await targetRepo.CommitAsync("Update dependencies", allowEmpty: false, cancellationToken: cancellationToken);
            }
        }

        return result with
        {
            // For already existing PRs, we want to always push the changes (even if only the <Source> tag changed)
            HadUpdates = result.HadUpdates || headBranchExisted,
        };
    }

    protected override bool ShouldResetClones => true;

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

        if (conflicts.Count == 0)
        {
            // When we do the rebase flow, we need only stage locally (in darc) after we rebase the work branch
            // In the service, we need to commit too so that we push the update to the PR
            await targetRepo.CommitAsync(commitMessage, allowEmpty: true, cancellationToken: cancellationToken);
        }

        return conflicts;
    }
}
