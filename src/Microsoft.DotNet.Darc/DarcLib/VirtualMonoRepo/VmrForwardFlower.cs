// Licensed to the .NET Foundation under one or more agreements.
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
    /// <param name="skipMeaninglessUpdates">Skip creating PR if only insignificant changes are present</param>
    /// <returns>CodeFlowResult containing information about the codeflow calculation</returns>
    Task<CodeFlowResult> FlowForwardAsync(
        string mapping,
        NativePath sourceRepo,
        Build buildToFlow,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        string targetVmrUri,
        bool skipMeaninglessUpdates = false,
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
            IProcessManager processManager,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, localGitRepoFactory, versionDetailsParser, logger)
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
        bool skipMeaninglessUpdates = false,
        CancellationToken cancellationToken = default)
    {
        ILocalGitRepo sourceRepo = _localGitRepoFactory.Create(repoPath);
        (bool headBranchExisted, LastFlows lastFlows) = await PrepareHeadBranch(targetVmrUri, mappingName, sourceRepo, targetBranch, headBranch, cancellationToken);

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ISourceComponent repoInfo = _sourceManifest.GetRepoVersion(mapping.Name);
        await sourceRepo.FetchAllAsync([mapping.DefaultRemote, repoInfo.RemoteUri], cancellationToken);
        await sourceRepo.CheckoutAsync(build.Commit);

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
                lastFlows.CrossingFlow,
                cancellationToken);
        }

        // We try to detect if the changes were meaningful and it's worth creating a new PR
        if (conflictedFiles != null && skipMeaninglessUpdates && hasChanges && !headBranchExisted)
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

            SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
            LastFlows lastFlows = await GetLastFlowsAsync(mapping, sourceRepo, currentIsBackflow: false);
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

            SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
            LastFlows lastFlows = await GetLastFlowsAsync(mapping, sourceRepo, currentIsBackflow: false);
            await vmr.CheckoutAsync(lastFlows.LastFlow.VmrSha);
            await vmr.CreateBranchAsync(headBranch, overwriteExistingBranch: true);
            return (false, lastFlows);
        }
    }

    protected override async Task<bool> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        bool hadUpdates;

        try
        {
            hadUpdates = await _vmrUpdater.UpdateRepository(
                mapping,
                build,
                resetToRemoteWhenCloningRepo: ShouldResetClones,
                cancellationToken: cancellationToken);
        }
        catch (PatchApplicationFailedException e)
        {
            // When we are updating an already existing PR branch, there can be conflicting changes in the PR from devs.
            if (headBranchExisted)
            {
                _logger.LogInformation("Failed to update a PR branch because of a conflict. Stopping the flow..");
                throw new ConflictInPrBranchException(e.Result.StandardError, targetBranch, mapping.Name, isForwardFlow: true);
            }

            // This happens when a conflicting change was made in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/dotnet/tree/main/docs/VMR-Full-Code-Flow.md#conflicts
            hadUpdates = await RecreatePreviousFlowAndApplyBuild(
                mapping,
                lastFlow,
                headBranch,
                sourceRepo,
                excludedAssets,
                targetBranch,
                build,
                headBranchExisted,
                cancellationToken);
        }

        return hadUpdates;
    }

    protected override async Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        Build build,
        string targetBranch,
        string headBranch,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        await sourceRepo.CheckoutAsync(lastFlow.RepoSha);

        var patchName = _vmrInfo.TmpPath / $"{headBranch.Replace('/', '-')}.patch";
        var branchName = currentFlow.GetBranchName();

        // TODO https://github.com/dotnet/arcade-services/issues/5030
        // This is only a temporary band aid solution, we should figure out the best way to fix the algorithm so the flow continues as expected 
        await CheckManualCommitsInBranch(sourceRepo, headBranch, targetBranch);

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to the VMR, we remove all files but the cloaked files
        List<string> removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule)
        ];

        var result = await _processManager.Execute(
            _processManager.GitExecutable,
            ["rm", "-r", "-q", "--", .. removalFilters],
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            cancellationToken: cancellationToken);

        result.ThrowIfFailed($"Failed to remove files from {sourceRepo}");

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

        return await _vmrUpdater.UpdateRepository(
            mapping,
            build,
            fromSha: currentSha,
            resetToRemoteWhenCloningRepo: ShouldResetClones,
            cancellationToken: cancellationToken);
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

    public async Task CheckManualCommitsInBranch(ILocalGitRepo sourceRepo, string headBranch, string targetBranch)
    {
        // If we have the target branch checked out as a local use it (in darc scenarios), otherwise use the remote one
        var result = await _processManager.ExecuteGit(
            _vmrInfo.VmrPath,
            [
                "rev-parse",
                targetBranch,
            ]);

        var fullTargetBranch = result.Succeeded ? targetBranch : $"origin/{targetBranch}";

        result = await _processManager.ExecuteGit(
            _vmrInfo.VmrPath,
            [
                "log",
                "--reverse",
                "--pretty=format:\"%H %an\"",
                $"{fullTargetBranch}..{headBranch}"]);

        result.ThrowIfFailed($"Failed to get commits from {targetBranch} to HEAD in {sourceRepo.Path}");
        // splits the output into 
        List<(string sha, string commiter)> headBranchCommits = result.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries) // split by lines
            .Select(line => line.Trim('\"'))
            .Select(line => line.Split(' ', 2)) // split by space, but only once
            .Select(l => (l[0], l[1]))
            .ToList();

        // if the first commit in the head branch wasn't made by the bot don't check, we might be in a test
        if (headBranchCommits.Any() && headBranchCommits[0].commiter != Constants.DefaultCommitAuthor)
        {
            return;
        }
        var manualCommits = headBranchCommits.Where(c => c.commiter != Constants.DefaultCommitAuthor);
        if (manualCommits.Any())
        {
            throw new ManualCommitsInFlowException(manualCommits.Select(c => c.sha).ToList());
        }
    }

    private async Task<bool> RecreatePreviousFlowAndApplyBuild(
        SourceMapping mapping,
        Codeflow lastFlow,
        string headBranch,
        ILocalGitRepo sourceRepo,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        Build build,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating the previous flow..");

        // Create a fake previously applied build. We only care about the sha here, because it will get overwritten anyway
        Build previouslyAppliedBuild = new(-1, DateTimeOffset.Now, 0, false, false, lastFlow.SourceSha, [], [], [], [])
        {
            GitHubRepository = build.GitHubRepository,
            AzureDevOpsRepository = build.AzureDevOpsRepository
        };

        // Find the VMR sha before the last successful flow
        var previousFlowTargetSha = await _localGitClient.BlameLineAsync(
            _vmrInfo.SourceManifestPath,
            line => line.Contains(lastFlow.SourceSha),
            lastFlow.TargetSha);

        await _localGitClient.ResetWorkingTree(_vmrInfo.VmrPath);
        var vmr = await _vmrCloneManager.PrepareVmrAsync(
            [_vmrInfo.VmrUri],
            [previousFlowTargetSha],
            previousFlowTargetSha,
            resetToRemote: false,
            cancellationToken);

        await vmr.CreateBranchAsync(headBranch, overwriteExistingBranch: true);

        LastFlows lastLastFlows = await GetLastFlowsAsync(mapping, sourceRepo, currentIsBackflow: lastFlow is Backflow);

        // Reconstruct the previous flow's branch
        await FlowCodeAsync(
            lastLastFlows,
            lastFlow,
            sourceRepo,
            mapping,
            previouslyAppliedBuild,
            excludedAssets,
            targetBranch,
            headBranch,
            headBranchExisted,
            cancellationToken);

        // We apply the current changes on top again - they should apply now
        try
        {
            return await _vmrUpdater.UpdateRepository(
                mapping,
                build,
                resetToRemoteWhenCloningRepo: ShouldResetClones,
                cancellationToken: cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogCritical("Failed to apply changes on top of previously recreated code flow: {message}", e.Message);
            throw;
        }
    }

    protected override NativePath GetEngCommonPath(NativePath sourceRepo) => sourceRepo / Constants.CommonScriptFilesPath;
    protected override bool TargetRepoIsVmr() => true;

    // When flowing local repos, we should never reset branches to the remote ones, we might lose some changes devs wanted
    protected virtual bool ShouldResetVmr => false;
    // In forward flow, we're flowing a specific commit, so we should just check it out, no need to sync local branch to remote
    protected virtual bool ShouldResetClones => false;
}
