﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Class for flowing code from a source repo to the target branch of the VMR.
/// This class is used in the context of darc CLI as some behaviours around repo preparation differ.
/// </summary>
public interface IVmrForwardFlower : IVmrCodeFlower
{
    /// <summary>
    /// Flows forward the code from the source repo to the target branch of the VMR.
    /// This overload is used in the context of the darc CLI.
    /// </summary>
    /// <param name="mapping">Mapping to flow</param>
    /// <param name="sourceRepo">Local checkout of the repository</param>
    /// <param name="buildToFlow">Build to flow</param>
    /// <param name="excludedAssets">Assets to exclude from the dependency flow</param>
    /// <param name="targetBranch">Target branch to create the PR against. If target branch does not exist, it is created off of this branch</param>
    /// <param name="headBranch">New/existing branch to make the changes on</param>
    /// <param name="targetVmrUri">URI of the VMR to update</param>
    /// <param name="rebase">Rebases changes (and leaves conflict markers in place) instead of recreating the previous flows recursively</param>
    /// <param name="forceUpdate">Force the update to be performed</param>
    /// <returns>CodeFlowResult containing information about the codeflow calculation</returns>
    Task<CodeFlowResult> FlowForwardAsync(
        string mapping,
        NativePath sourceRepo,
        Build buildToFlow,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        string targetVmrUri,
        bool rebase,
        bool forceUpdate,
        CancellationToken cancellationToken = default);
}

public class VmrForwardFlower : VmrCodeFlower, IVmrForwardFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly ICodeFlowVmrUpdater _vmrUpdater;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly ILocalGitClient _localGitClient;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly ICodeflowChangeAnalyzer _codeflowChangeAnalyzer;
    private readonly IForwardFlowConflictResolver _conflictResolver;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IProcessManager _processManager;
    private readonly ILogger<VmrCodeFlower> _logger;

    public VmrForwardFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            ICodeFlowVmrUpdater vmrUpdater,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
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
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, localGitRepoFactory, versionDetailsParser, fileSystem, commentCollector, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _vmrUpdater = vmrUpdater;
        _dependencyTracker = dependencyTracker;
        _vmrCloneManager = vmrCloneManager;
        _localGitClient = localGitClient;
        _localGitRepoFactory = localGitRepoFactory;
        _codeflowChangeAnalyzer = codeflowChangeAnalyzer;
        _conflictResolver = conflictResolver;
        _workBranchFactory = workBranchFactory;
        _processManager = processManager;
        _logger = logger;
    }

    public async Task<CodeFlowResult> FlowForwardAsync(
        string mappingName,
        NativePath repoPath,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        string targetVmrUri,
        bool rebase,
        bool forceUpdate,
        CancellationToken cancellationToken = default)
    {
        ILocalGitRepo sourceRepo = _localGitRepoFactory.Create(repoPath);
        (bool headBranchExisted, LastFlows lastFlows) = await PrepareHeadBranch(
            targetVmrUri,
            mappingName,
            sourceRepo,
            targetBranch,
            headBranch,
            cancellationToken);

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ISourceComponent repoInfo = _sourceManifest.GetRepoVersion(mapping.Name);

        await sourceRepo.FetchAllAsync([mapping.DefaultRemote, repoInfo.RemoteUri], cancellationToken);

        ForwardFlow currentFlow = new(build.Commit, lastFlows.LastFlow.VmrSha);

        bool hasChanges = await FlowCodeAsync(
            lastFlows,
            currentFlow,
            sourceRepo,
            mapping,
            build,
            excludedAssets,
            targetBranch,
            headBranch,
            headBranchExisted,
            rebase,
            forceUpdate,
            cancellationToken);

        IReadOnlyCollection<UnixPath>? conflictedFiles = null;
        if (hasChanges)
        {
            // We try to merge the target branch so that we can potentially
            // resolve some expected conflicts in the version files
            ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
            conflictedFiles = await _conflictResolver.TryMergingBranch(
                mapping.Name,
                vmr,
                sourceRepo,
                headBranch,
                targetBranch,
                currentFlow,
                lastFlows,
                rebase,
                cancellationToken);
        }

        // If we don't force the update, we'll set hasChanges to false when the updates are not meaningful
        if (conflictedFiles != null && !forceUpdate && hasChanges && !headBranchExisted)
        {
            hasChanges &= await _codeflowChangeAnalyzer.ForwardFlowHasMeaningfulChangesAsync(mapping.Name, headBranch, targetBranch);
        }

        return new CodeFlowResult(
            hasChanges,
            conflictedFiles ?? [],
            sourceRepo.Path,
            DependencyUpdates: []);
    }

    /// <summary>
    /// Clones the VMR and tries to check out a given head branch.
    /// If fails, checks out the base branch instead, finds the last synchronization point
    /// and creates the head branch at that point.
    /// </summary>
    /// <returns>True if the head branch already existed</returns>
    private async Task<(bool, LastFlows)> PrepareHeadBranch(
        string vmrUri,
        string mappingName,
        ILocalGitRepo sourceRepo,
        string baseBranch,
        string headBranch,
        CancellationToken cancellationToken)
    {
        _vmrInfo.VmrUri = vmrUri;

        try
        {
            await _vmrCloneManager.PrepareVmrAsync(
                [vmrUri],
                [baseBranch, headBranch],
                headBranch,
                ShouldResetVmr,
                cancellationToken);

            LastFlows lastFlows = await GetLastFlowsAsync(mappingName, sourceRepo, currentIsBackflow: false);
            return (true, lastFlows);
        }
        catch (NotFoundException)
        {
            // If the head branch does not exist, we need to create it at the point of the last sync
            ILocalGitRepo vmr;
            try
            {
                vmr = await _vmrCloneManager.PrepareVmrAsync(
                    [vmrUri],
                    [baseBranch],
                    baseBranch,
                    ShouldResetVmr,
                    cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to find branch {branch} in {uri}", baseBranch, vmrUri);
                throw new TargetBranchNotFoundException($"Failed to find target branch {baseBranch} in {vmrUri}", e);
            }

            LastFlows lastFlows = await GetLastFlowsAsync(mappingName, sourceRepo, currentIsBackflow: false);

            // Rebase strategy works on top of the target branch, non-rebase starts from the last point of synchronization
            await vmr.CheckoutAsync(lastFlows.LastFlow.VmrSha);
            await _dependencyTracker.RefreshMetadataAsync();
            await vmr.CreateBranchAsync(headBranch, overwriteExistingBranch: true);

            return (false, lastFlows);
        }
    }

    protected override async Task<bool> SameDirectionFlowAsync(
        SourceMapping mapping,
        LastFlows lastFlows,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool headBranchExisted,
        bool rebase,
        bool forceUpdate,
        CancellationToken cancellationToken)
    {
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        IWorkBranch? workBranch = null;
        if (rebase || headBranchExisted)
        {
            await vmr.CheckoutAsync(lastFlows.LastFlow.VmrSha);

            workBranch = await _workBranchFactory.CreateWorkBranchAsync(vmr, currentFlow.GetBranchName(), headBranch);
        }

        bool hadChanges;

        async Task<bool> ReapplyLatestChanges()
        {
            hadChanges = await _vmrUpdater.UpdateRepository(
                mapping,
                build,
                additionalFileExclusions: [.. DependencyFileManager.CodeflowDependencyFiles],
                resetToRemoteWhenCloningRepo: ShouldResetClones,
                cancellationToken: cancellationToken);

            return hadChanges;
        }

        try
        {
            hadChanges = await ReapplyLatestChanges();
        }
        catch (PatchApplicationFailedException e)
        {
            hadChanges = false;

            if (rebase)
            {
                // We need to recreate a previous flow so that we have something to rebase later
                await RecreatePreviousFlowsAndApplyChanges(
                    mapping,
                    build,
                    sourceRepo,
                    currentFlow,
                    lastFlows,
                    workBranch!.WorkBranchName,
                    workBranch!.WorkBranchName,
                    excludedAssets,
                    forceUpdate,
                    ReapplyLatestChanges,
                    cancellationToken);

                // Workaround for files that can be left behind after HandleRevertedFiles()
                // It can be removed after we remove HandleRevertedFiles() and switch to rebase-only
                await vmr.ResetWorkingTree();
            }
            else
            {
                // When we are updating an already existing PR branch, there can be conflicting changes in the PR from devs.
                if (headBranchExisted)
                {
                    _logger.LogInformation("Failed to update a PR branch because of a conflict. Stopping the flow..");
                    throw new ConflictInPrBranchException(e.Result.StandardError, targetBranch, mapping.Name, isForwardFlow: true);
                }

                // This happens when a conflicting change was made in the last backflow PR (before merging)
                // The scenario is described here: https://github.com/dotnet/dotnet/tree/main/docs/VMR-Full-Code-Flow.md#conflicts
                await RecreatePreviousFlowsAndApplyChanges(
                    mapping,
                    build,
                    sourceRepo,
                    currentFlow,
                    lastFlows,
                    headBranch,
                    targetBranch,
                    excludedAssets,
                    forceUpdate,
                    ReapplyLatestChanges,
                    cancellationToken);

                if (hadChanges)
                {
                    // Commit anything staged only (e.g. reset reverted files)
                    await _localGitClient.CommitAmendAsync(_vmrInfo.VmrPath, cancellationToken);
                }
            }
        }

        if (!hadChanges || workBranch == null)
        {
            return hadChanges;
        }

        var commitMessage = (await vmr.RunGitCommandAsync(["log", "-1", "--pretty=%B"], cancellationToken)).StandardOutput;

        await MergeWorkBranchAsync(
            mapping,
            build,
            currentFlow,
            vmr,
            targetBranch,
            headBranch,
            workBranch,
            headBranchExisted,
            rebase,
            commitMessage,
            cancellationToken);

        return true;
    }

    protected override async Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        LastFlows lastFlows,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        Build build,
        string targetBranch,
        string headBranch,
        bool headBranchExisted,
        bool rebase,
        CancellationToken cancellationToken)
    {
        // If the target branch did not exist, checkout the last synchronization point
        // Otherwise, check out the last flow's commit in the PR branch
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        await vmr.CheckoutAsync(headBranchExisted && !rebase
            ? lastFlows.LastForwardFlow.VmrSha
            : lastFlows.LastFlow.VmrSha);

        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(
            vmr,
            currentFlow.GetBranchName(),
            headBranch);

        await sourceRepo.CheckoutAsync(lastFlows.LastFlow.RepoSha);

        var patchName = _vmrInfo.TmpPath / $"{headBranch.Replace('/', '-')}.patch";

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to the VMR, we remove all files but the cloaked files
        List<string> removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. DependencyFileManager.CodeflowDependencyFiles.Select(VmrPatchHandler.GetExclusionRule),
        ];

        var result = await _processManager.Execute(
            _processManager.GitExecutable,
            ["rm", "-r", "-q", "-f", "--", .. removalFilters],
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            cancellationToken: cancellationToken);

        result.ThrowIfFailed($"Failed to remove files from the VMR");

        // Save the current sha we're flowing from before changing it to the zero commit
        var currentSha = _dependencyTracker.GetDependencyVersion(mapping)?.Sha
            ?? throw new Exception($"Failed to find current sha for {mapping.Name}");

        // We make the VMR believe it has the zero commit of the repo as it matches the dir/git state at the moment
        _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
            mapping,
            build.GetRepository(),
            Constants.EmptyGitObject,
            Parent: null,
            build.AzureDevOpsBuildNumber,
            build.Id));

        bool hadChanges = await _vmrUpdater.UpdateRepository(
            mapping,
            build,
            additionalFileExclusions: [.. DependencyFileManager.CodeflowDependencyFiles],
            fromSha: currentSha,
            resetToRemoteWhenCloningRepo: ShouldResetClones,
            cancellationToken: cancellationToken);

        if (!hadChanges)
        {
            return hadChanges;
        }

        var commitMessage = (await vmr.RunGitCommandAsync(["log", "-1", "--pretty=%B"], cancellationToken)).StandardOutput;

        await MergeWorkBranchAsync(
            mapping,
            build,
            currentFlow,
            vmr,
            targetBranch,
            headBranch,
            workBranch,
            headBranchExisted,
            rebase,
            commitMessage,
            cancellationToken);

        return true;
    }

    protected override async Task<Codeflow?> DetectCrossingFlow(
        Codeflow lastFlow,
        Backflow? lastBackFlow,
        ForwardFlow lastForwardFlow,
        ILocalGitRepo repo)
    {
        if (lastFlow is not Backflow bf)
        {
            return null;
        }

        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        return await vmr.IsAncestorCommit(bf.VmrSha, lastForwardFlow.VmrSha)
            ? lastForwardFlow
            : null;
    }

    /// <summary>
    /// Traverses the current branch's history to find {depth}-th last backflow and creates a branch there.
    /// </summary>
    /// <returns>The {depth}-th last flow and its previous flows.</returns>
    protected override async Task<(Codeflow, LastFlows)> RewindToPreviousFlowAsync(
        SourceMapping mapping,
        ILocalGitRepo sourceRepo,
        int depth,
        LastFlows previousFlows,
        string branchToCreate,
        string targetBranch,
        CancellationToken cancellationToken)
    {
        var previousFlow = previousFlows.LastForwardFlow;

        for (int i = 1; i < depth; i++)
        {
            var previousFlowSha = await _localGitClient.BlameLineAsync(
                _vmrInfo.SourceManifestPath,
                line => line.Contains(previousFlow.RepoSha),
                previousFlow.VmrSha);

            await _localGitClient.ResetWorkingTree(_vmrInfo.VmrPath);
            await _vmrCloneManager.PrepareVmrAsync(
                [_vmrInfo.VmrUri],
                [previousFlowSha],
                previousFlowSha,
                resetToRemote: false,
                cancellationToken);

            await sourceRepo.CheckoutAsync(_sourceManifest.GetRepoVersion(mapping.Name).CommitSha);
            previousFlows = await GetLastFlowsAsync(mapping.Name, sourceRepo, currentIsBackflow: false);
            previousFlow = previousFlows.LastForwardFlow;
        }

        // Check out the VMR before the flows we want to recreate
        await _localGitClient.ResetWorkingTree(_vmrInfo.VmrPath);
        var vmr = await _vmrCloneManager.PrepareVmrAsync(
            [_vmrInfo.VmrUri],
            [previousFlow.VmrSha],
            previousFlow.VmrSha,
            resetToRemote: false,
            cancellationToken);

        await vmr.CreateBranchAsync(branchToCreate, overwriteExistingBranch: true);

        return (previousFlow, previousFlows);
    }

    protected override async Task EnsureCodeflowLinearityAsync(ILocalGitRepo repo, Codeflow currentFlow, LastFlows lastFlows)
    {
        var lastFlowRepoSha = lastFlows.LastForwardFlow.RepoSha;

        if (!await repo.IsAncestorCommit(lastFlowRepoSha, currentFlow.RepoSha))
        {
            throw new NonLinearCodeflowException(currentFlow.VmrSha, lastFlowRepoSha);
        }
    }

    protected override NativePath GetEngCommonPath(NativePath sourceRepo) => sourceRepo / Constants.CommonScriptFilesPath;
    protected override bool TargetRepoIsVmr() => true;

    // When flowing local repos, we should never reset branches to the remote ones, we might lose some changes devs wanted
    protected virtual bool ShouldResetVmr => false;
    // In forward flow, we're flowing a specific commit, so we should just check it out, no need to sync local branch to remote
    protected virtual bool ShouldResetClones => false;
}
