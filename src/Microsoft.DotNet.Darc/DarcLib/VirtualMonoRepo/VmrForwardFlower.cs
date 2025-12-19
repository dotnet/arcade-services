// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Maestro.Common;
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
    /// <param name="enableRebase">Rebases changes (and leaves conflict markers in place) instead of recreating the previous flows recursively</param>
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
        bool enableRebase,
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
    private readonly ICommentCollector _commentCollector;
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
        _commentCollector = commentCollector;
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
        bool enableRebase,
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
            enableRebase,
            cancellationToken);

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ISourceComponent repoInfo = _sourceManifest.GetRepoVersion(mapping.Name);

        await sourceRepo.FetchAllAsync([mapping.DefaultRemote, repoInfo.RemoteUri], cancellationToken);

        ForwardFlow currentFlow = new(build.Commit, lastFlows.LastFlow.VmrSha);
        CodeflowOptions codeflowOptions = new(mapping, currentFlow, targetBranch, headBranch, build, excludedAssets, enableRebase, forceUpdate);

        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        cancellationToken.ThrowIfCancellationRequested();

        CodeFlowResult result = await FlowCodeAsync(codeflowOptions, lastFlows, sourceRepo, headBranchExisted, cancellationToken) with
        {
            RepoPath = sourceRepo.Path
        };

        if (result.HadUpdates)
        {
            LastFlows lastFlowsAfterRecreation = lastFlows;
            if (codeflowOptions.EnableRebase && lastFlows.CrossingFlow == null && result.RecreatedPreviousFlows)
            {
                lastFlowsAfterRecreation = lastFlows with
                {
                    CrossingFlow = lastFlows.LastForwardFlow,
                };
            }

            // We try to merge the target branch so that we can potentially
            // resolve some expected conflicts in the version files
            result = result with
            {
                ConflictedFiles = await _conflictResolver.TryMergingBranchAndUpdateDependencies(
                    codeflowOptions,
                    vmr,
                    sourceRepo,
                    lastFlowsAfterRecreation,
                    headBranchExisted,
                    cancellationToken)
            };

            await CommentIncludedPRs(sourceRepo, lastFlows.LastForwardFlow.RepoSha, build.Commit, mapping.DefaultRemote, cancellationToken);
        }

        // If we don't force the update, we'll set hasChanges to false when the updates are not meaningful
        if (!result.HadConflicts && !forceUpdate && result.HadUpdates && !headBranchExisted)
        {
            result = result with
            {
                HadUpdates = await _codeflowChangeAnalyzer.ForwardFlowHasMeaningfulChangesAsync(mapping.Name, headBranch, targetBranch)
            };
        }

        return result;
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
        bool enableRebase,
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
            if (!enableRebase)
            {
                await vmr.CheckoutAsync(lastFlows.LastFlow.VmrSha);
                await _dependencyTracker.RefreshMetadataAsync();
            }

            await vmr.CreateBranchAsync(headBranch, overwriteExistingBranch: true);

            return (false, lastFlows);
        }
    }

    protected override async Task<CodeFlowResult> SameDirectionFlowAsync(
        CodeflowOptions codeflowOptions,
        LastFlows lastFlows,
        ILocalGitRepo sourceRepo,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        IWorkBranch? workBranch = null;
        if (codeflowOptions.EnableRebase || headBranchExisted)
        {
            await vmr.CheckoutAsync(lastFlows.LastFlow.VmrSha);

            workBranch = await _workBranchFactory.CreateWorkBranchAsync(vmr, codeflowOptions.CurrentFlow.GetBranchName(), codeflowOptions.HeadBranch);
        }

        CodeFlowResult result = await ApplyChangesWithRecreationFallbackAsync(
            codeflowOptions,
            lastFlows,
            sourceRepo,
            headBranchExisted,
            workBranch,
            async keepConflicts =>
                await _vmrUpdater.UpdateRepository(
                    codeflowOptions.Mapping,
                    codeflowOptions.Build,
                    additionalFileExclusions: [.. DependencyFileManager.CodeflowDependencyFiles],
                    resetToRemoteWhenCloningRepo: ShouldResetClones,
                    keepConflicts: keepConflicts,
                    cancellationToken: cancellationToken),
            cancellationToken);

        if (workBranch != null)
        {
            var commitMessage = (await vmr.RunGitCommandAsync(["log", "-1", "--pretty=%B"], cancellationToken)).StandardOutput;

            result = result with
            {
                ConflictedFiles = await MergeWorkBranchAsync(
                    codeflowOptions,
                    vmr,
                    workBranch,
                    headBranchExisted,
                    commitMessage,
                    cancellationToken)
            };
        }

        return result;
    }

    protected override async Task<CodeFlowResult> OppositeDirectionFlowAsync(
        CodeflowOptions codeflowOptions,
        LastFlows lastFlows,
        ILocalGitRepo sourceRepo,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        // If the target branch did not exist, checkout the last synchronization point
        // Otherwise, check out the last flow's commit in the PR branch
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        await vmr.CheckoutAsync(headBranchExisted && !codeflowOptions.EnableRebase
            ? lastFlows.LastForwardFlow.VmrSha
            : lastFlows.LastFlow.VmrSha);

        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(
            vmr,
            codeflowOptions.CurrentFlow.GetBranchName(),
            codeflowOptions.HeadBranch);

        await sourceRepo.CheckoutAsync(lastFlows.LastFlow.RepoSha);

        var patchName = _vmrInfo.TmpPath / $"{codeflowOptions.HeadBranch.Replace('/', '-')}.patch";

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to the VMR, we remove all files but the cloaked files
        List<string> removalFilters =
        [
            .. codeflowOptions.Mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. codeflowOptions.Mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. DependencyFileManager.CodeflowDependencyFiles.Select(VmrPatchHandler.GetExclusionRule),
        ];

        (await _processManager
            .Execute(
                _processManager.GitExecutable,
                ["rm", "-r", "-q", "-f", "--", .. removalFilters],
                workingDir: _vmrInfo.GetRepoSourcesPath(codeflowOptions.Mapping),
                cancellationToken: cancellationToken))
            .ThrowIfFailed($"Failed to remove files from the VMR");

        // Save the current sha we're flowing from before changing it to the zero commit
        var currentSha = _dependencyTracker.GetDependencyVersion(codeflowOptions.Mapping)?.Sha
            ?? throw new Exception($"Failed to find current sha for {codeflowOptions.Mapping.Name}");

        // We make the VMR believe it has the zero commit of the repo as it matches the dir/git state at the moment
        _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
            codeflowOptions.Mapping,
            codeflowOptions.Build.GetRepository(),
            Constants.EmptyGitObject,
            Parent: null,
            codeflowOptions.Build.AzureDevOpsBuildNumber,
            codeflowOptions.Build.Id));

        CodeFlowResult result = await _vmrUpdater.UpdateRepository(
            codeflowOptions.Mapping,
            codeflowOptions.Build,
            additionalFileExclusions: [.. DependencyFileManager.CodeflowDependencyFiles],
            fromSha: currentSha,
            keepConflicts: codeflowOptions.EnableRebase,
            resetToRemoteWhenCloningRepo: ShouldResetClones,
            cancellationToken: cancellationToken);

        if (result.HadUpdates)
        {
            var commitMessage = (await vmr.RunGitCommandAsync(["log", "-1", "--pretty=%B"], cancellationToken)).StandardOutput;

            result = result with
            {
                ConflictedFiles = await MergeWorkBranchAsync(
                    codeflowOptions,
                    vmr,
                    workBranch,
                    headBranchExisted,
                    commitMessage,
                    cancellationToken)
            };
        }

        return result;
    }

    private async Task CommentIncludedPRs(
        ILocalGitRepo sourceRepo,
        string lastCommit,
        string currentCommit,
        string repoUri,
        CancellationToken cancellationToken)
    {
        var gitRepoType = GitRepoUrlUtils.ParseTypeFromUri(repoUri);
        // codeflow tests set the defaultRemote to a local path, we have to skip those
        if (gitRepoType == GitRepoType.Local)
        {
            return;
        }

        var result = await sourceRepo.ExecuteGitCommand(["log", "--pretty=%s", $"{lastCommit}..{currentCommit}"], cancellationToken);
        result.ThrowIfFailed($"Failed to get the list of commits between {lastCommit} and {currentCommit} in {sourceRepo.Path}");

        var commitMessages = result.GetOutputLines();
        var prsInfo = GitRepoUtils.ExtractPullRequestUrisFromCommitTitles(commitMessages, repoUri);

        if (prsInfo.Count == 0)
        {
            _logger.LogInformation("No PR numbers were found in the commit messages between {lastCommit} and {currentCommit}", lastCommit, currentCommit);
            return;
        }
        else
        {
            StringBuilder str = new("PRs from original repository included in this codeflow update:");
            foreach (var prInfo in prsInfo.Distinct())
            {
                string format = $"- {{0}}";
                str.AppendLine();
                str.AppendFormat(format, prInfo.prUri);
            }

            _commentCollector.AddComment(str.ToString(), CommentType.Information);
        }
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

    protected override async Task<(Codeflow, LastFlows)> UnwindPreviousFlowAsync(
        SourceMapping mapping,
        ILocalGitRepo sourceRepo,
        LastFlows previousFlows,
        string branchToCreate,
        string targetBranch,
        CancellationToken cancellationToken)
    {
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        ForwardFlow previousFlow = previousFlow = previousFlows.LastForwardFlow;
        await _vmrCloneManager.PrepareVmrAsync(
            [_vmrInfo.VmrUri],
            [previousFlow.VmrSha],
            previousFlow.VmrSha,
            resetToRemote: false,
            cancellationToken);

        await sourceRepo.ForceCheckoutAsync(previousFlow.RepoSha);

        var previousFlowSha = await _localGitClient.BlameLineAsync(
            _vmrInfo.SourceManifestPath,
            line => line.Contains(previousFlow.RepoSha),
            previousFlow.VmrSha);

        await _localGitClient.ResetWorkingTree(_vmrInfo.VmrPath);
        vmr = await _vmrCloneManager.PrepareVmrAsync(
            [_vmrInfo.VmrUri],
            [previousFlowSha],
            previousFlowSha,
            resetToRemote: false,
            cancellationToken);

        await sourceRepo.ForceCheckoutAsync(_sourceManifest.GetRepoVersion(mapping.Name).CommitSha);
        previousFlows = await GetLastFlowsAsync(mapping.Name, sourceRepo, currentIsBackflow: false);
        previousFlow = previousFlows.LastForwardFlow;

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
