// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibGit2Sharp;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
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
            IVersionFileCodeFlowUpdater versionFileConflictResolver,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, vmrCloneManager, repositoryCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, vmrPatchHandler, workBranchFactory, versionFileConflictResolver, fileSystem, barClient, logger)
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
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        (var headBranchExisted, SourceMapping mapping, ILocalGitRepo targetRepo) = await PrepareVmrAndRepo(
            subscription,
            build,
            targetBranch,
            cancellationToken);

        var lastFlows = await GetLastFlowsAsync(mapping, targetRepo, currentIsBackflow: true);

        var result = await FlowBackAsync(
            mapping,
            targetRepo,
            lastFlows.LastFlow,
            build,
            subscription.ExcludedAssets,
            subscription.TargetBranch,
            targetBranch,
            discardPatches: true,
            headBranchExisted,
            cancellationToken);

        IReadOnlyCollection<UpstreamRepoDiff> repoUpdates = await ComputeRepoUpdatesAsync(lastFlows.LastBackFlow?.VmrSha, build.Commit);

        return result with
        {
            // For already existing PRs, we want to always push the changes (even if only the <Source> tag changed)
            HadUpdates = result.HadUpdates || headBranchExisted,
            UpstreamRepoDiffs = repoUpdates
        };
    }

    private async Task<(bool, SourceMapping, ILocalGitRepo)> PrepareVmrAndRepo(
        Subscription subscription,
        Build build,
        string targetBranch,
        CancellationToken cancellationToken)
    {
        // Prepare the VMR
        await _vmrCloneManager.PrepareVmrAsync(
            [build.GetRepository()],
            [build.Commit],
            build.Commit,
            ShouldResetVmr,
            cancellationToken);

        // Prepare repo
        SourceMapping mapping = _dependencyTracker.GetMapping(subscription.SourceDirectory);
        var remotes = new[]
            {
                mapping.DefaultRemote,
                _sourceManifest.GetRepoVersion(mapping.Name).RemoteUri,
                subscription.TargetRepository
            }
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToList();

        ILocalGitRepo targetRepo;
        bool headBranchExisted;

        // Now try to see if the target branch exists already
        try
        {
            targetRepo = await _repositoryCloneManager.PrepareCloneAsync(
                mapping,
                remotes,
                [subscription.TargetBranch, targetBranch],
                targetBranch,
                ShouldResetClones,
                cancellationToken);
            headBranchExisted = true;
        }
        catch (NotFoundException)
        {
            // If target branch does not exist, we create it off of the base branch
            targetRepo = await _repositoryCloneManager.PrepareCloneAsync(
                mapping,
                remotes,
                subscription.TargetBranch,
                ShouldResetClones,
                cancellationToken);
            await targetRepo.CreateBranchAsync(targetBranch);
            headBranchExisted = false;
        }

        return (headBranchExisted, mapping, targetRepo);
    }

    private async Task<IReadOnlyCollection<UpstreamRepoDiff>> ComputeRepoUpdatesAsync(string? lastFlowSha, string currentFlowSha)
    {
        _logger.LogInformation("Computing repo updates between {LastFlowSha} and {CurrentFlowSha}", lastFlowSha, currentFlowSha);

        if (string.IsNullOrEmpty(lastFlowSha) || string.IsNullOrEmpty(currentFlowSha))
        {
            _logger.LogError("Aborting repo diff calculation. last flow sha and current flow sha are identical");
            return [];
        }

        SourceManifest? oldSrcManifest = null;
        SourceManifest? newSrcManifest = null;

        string? oldFileContents = await _localGitClient.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceManifestPath, VmrInfoInstance.VmrPath, lastFlowSha);
        if (oldFileContents != null)
        {
            oldSrcManifest = SourceManifest.FromJson(oldFileContents);
        }

        string? newFileContents = await _localGitClient.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceManifestPath, VmrInfoInstance.VmrPath, currentFlowSha);
        if (newFileContents != null)
        {
            newSrcManifest = SourceManifest.FromJson(newFileContents);
        }

        if (oldSrcManifest != null && newSrcManifest != null)
        {
            var oldRepos = oldSrcManifest.Repositories.ToDictionary(r => r.RemoteUri ?? r.Path, r => r.CommitSha);
            var newRepos = newSrcManifest.Repositories.ToDictionary(r => r.RemoteUri ?? r.Path, r => r.CommitSha);


            var allKeys = oldRepos.Keys.Union(newRepos.Keys);

            var upstreamRepoDiffs = allKeys
                .Select(key => new UpstreamRepoDiff(
                    key,
                    oldRepos.TryGetValue(key, out var oldSha) ? oldSha : null,
                    newRepos.TryGetValue(key, out var newSha) ? newSha : null
                ))
                .Where(x => x.OldCommitSha != x.NewCommitSha)
                .ToList();

            UpstreamRepoDiff vmrDiff = new UpstreamRepoDiff(
                VmrInfoInstance.VmrUri,
                lastFlowSha,
                currentFlowSha);

            return [vmrDiff,
                ..upstreamRepoDiffs];
        }
        return [];
    }

    // During backflow, we're targeting a specific repo branch, so we should make sure we reset local branch to the remote one
    private const bool ShouldResetClones = true;
}
