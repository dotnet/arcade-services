﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
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

public interface IVmrBackFlower : IVmrCodeFlower
{
    /// <summary>
    /// Flows backward the code from the VMR to the target branch of a product repo.
    /// This overload is used in the context of the darc CLI.
    /// </summary>
    /// <param name="mapping">Mapping to flow</param>
    /// <param name="targetRepo">Local checkout of the repository</param>
    /// <param name="buildToFlow">Build to flow</param>
    /// <param name="excludedAssets">Assets to exclude from the dependency flow</param>
    /// <param name="targetBranch">Target branch to create the PR against. If target branch does not exist, it is created off of this branch</param>
    /// <param name="headBranch">New/existing branch to make the changes on</param>
    /// <param name="enableRebase">Rebases changes (and leaves conflict markers in place) instead of recreating the previous flows recursively</param>
    /// <param name="forceUpdate">Apply updates always, even when no or non-meaningful changes only are flown</param>
    Task<CodeFlowResult> FlowBackAsync(
        string mapping,
        NativePath targetRepo,
        Build buildToFlow,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool enableRebase,
        bool forceUpdate,
        CancellationToken cancellationToken = default);
}

public class VmrBackFlower : VmrCodeFlower, IVmrBackFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly IRepositoryCloneManager _repositoryCloneManager;
    private readonly ILocalGitClient _localGitClient;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IBackflowConflictResolver _conflictResolver;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeFlower> _logger;

    public VmrBackFlower(
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
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, localGitRepoFactory, versionDetailsParser, fileSystem, commentCollector, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _vmrCloneManager = vmrCloneManager;
        _repositoryCloneManager = repositoryCloneManager;
        _localGitClient = localGitClient;
        _localGitRepoFactory = localGitRepoFactory;
        _vmrPatchHandler = vmrPatchHandler;
        _workBranchFactory = workBranchFactory;
        _conflictResolver = versionFileConflictResolver;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<CodeFlowResult> FlowBackAsync(
        string mappingName,
        NativePath targetRepoPath,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool enableRebase,
        bool forceUpdate,
        CancellationToken cancellationToken = default)
    {
        (bool headBranchExisted, SourceMapping mapping, LastFlows lastFlows, ILocalGitRepo targetRepo) = await PrepareVmrAndRepo(
            mappingName,
            build,
            targetBranch,
            headBranch,
            targetRepoPath,
            enableRebase,
            cancellationToken);

        return await FlowBackAsync(
            new CodeflowOptions(mapping, targetBranch, headBranch, build, excludedAssets, enableRebase, forceUpdate),
            targetRepo,
            lastFlows,
            headBranchExisted,
            cancellationToken);
    }

    protected async Task<CodeFlowResult> FlowBackAsync(
        CodeflowOptions codeflow,
        ILocalGitRepo targetRepo,
        LastFlows lastFlows,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var currentFlow = new Backflow(codeflow.Build.Commit, lastFlows.LastFlow.RepoSha);
        var hasChanges = await FlowCodeAsync(
            codeflow,
            lastFlows,
            currentFlow,
            targetRepo,
            headBranchExisted,
            cancellationToken);

        // We try to merge the target branch and we apply dependency updates
        VersionFileUpdateResult mergeResult = await _conflictResolver.TryMergingBranchAndUpdateDependencies(
            codeflow,
            lastFlows,
            currentFlow,
            targetRepo,
            codeflow.TargetBranch,
            headBranchExisted,
            cancellationToken);

        return new CodeFlowResult(
            hasChanges || mergeResult.DependencyUpdates.Count > 0,
            mergeResult.ConflictedFiles,
            targetRepo.Path,
            mergeResult.DependencyUpdates);
    }

    protected override async Task<bool> SameDirectionFlowAsync(
        CodeflowOptions codeflow,
        LastFlows lastFlows,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var lastFlownSha = lastFlows.LastFlow.VmrSha;
        var patchName = GetPatchName(codeflow.Mapping, lastFlows, currentFlow);

        // When flowing from the VMR, ignore all submodules
        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            lastFlownSha,
            currentFlow.VmrSha,
            path: null,
            filters: GetPatchExclusions(codeflow.Mapping),
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(codeflow.Mapping),
            applicationPath: null,
            ignoreLineEndings: false,
            cancellationToken);

        if (patches.Count == 0 || patches.All(p => _fileSystem.GetFileInfo(p.Path).Length == 0))
        {
            _logger.LogInformation("There are no new changes for VMR between {sha1} and {sha2}",
                lastFlownSha,
                currentFlow.VmrSha);

            foreach (VmrIngestionPatch patch in patches)
            {
                try
                {
                    _fileSystem.DeleteFile(patch.Path);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to delete patch file {patchPath}", patch.Path);
                }
            }

            return false;
        }

        _logger.LogDebug("Created {count} patch(es)", patches.Count);

        IWorkBranch? workBranch = null;
        if (codeflow.EnableRebase || headBranchExisted)
        {
            await targetRepo.CheckoutAsync(lastFlows.LastFlow.RepoSha);

            workBranch = await _workBranchFactory.CreateWorkBranchAsync(
                targetRepo,
                currentFlow.GetBranchName(),
                codeflow.HeadBranch);
        }

        await ApplyChangesWithRecreationFallbackAsync(
            codeflow,
            lastFlows,
            currentFlow,
            targetRepo,
            headBranchExisted,
            workBranch,
            async keepConflicts =>
            {
                await _vmrPatchHandler.ApplyPatches(
                    patches,
                    targetRepo.Path,
                    removePatchAfter: true,
                    keepConflicts: keepConflicts,
                    cancellationToken: cancellationToken);
            },
            cancellationToken);

        if (workBranch != null)
        {
            var commitMessage = await CommitBackflow(currentFlow, targetRepo, codeflow.Build, cancellationToken);

	        await MergeWorkBranchAsync(
	            codeflow,
	            currentFlow,
	            targetRepo,
	            workBranch,
	            headBranchExisted,
	            commitMessage,
	            cancellationToken);
        }

        return true;
    }

    protected override async Task<bool> OppositeDirectionFlowAsync(
        CodeflowOptions codeflow,
        LastFlows lastFlows,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        // If the target branch did not exist, checkout the last synchronization point
        // Otherwise, check out the last flow's commit in the PR branch
        await targetRepo.CheckoutAsync(headBranchExisted && !codeflow.EnableRebase
            ? lastFlows.LastBackFlow!.RepoSha
            : lastFlows.LastFlow.RepoSha);

        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(
            targetRepo,
            currentFlow.GetBranchName(),
            codeflow.HeadBranch);

        // We leave the inlined submodules in the VMR
        var exclusions = GetPatchExclusions(codeflow.Mapping);
        var patchName = GetPatchName(codeflow.Mapping, lastFlows, currentFlow);

        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            Constants.EmptyGitObject,
            currentFlow.VmrSha,
            path: null,
            filters: exclusions,
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(codeflow.Mapping),
            applicationPath: null,
            ignoreLineEndings: false,
            cancellationToken);

        _logger.LogDebug("Created {count} patch(es)", patches.Count);

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to a repo, we remove all repo files but submodules and cloaked files
        List<string> removalFilters =
        [
            .. codeflow.Mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. codeflow.Mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. exclusions,
        ];

        string[] args = ["rm", "-r", "-q", "-f"];
        if (removalFilters.Count > 0)
        {
            args = [.. args, "--", .. removalFilters];
        }
        else
        {
            args = [.. args, "."];
        }

        ProcessExecutionResult result = await targetRepo.ExecuteGitCommand(args, cancellationToken);
        result.ThrowIfFailed($"Failed to remove files from {targetRepo}");

        // Now we insert the VMR files
        await _vmrPatchHandler.ApplyPatches(
            patches,
            targetRepo.Path,
            removePatchAfter: true,
            keepConflicts: false,
            cancellationToken: cancellationToken);

        // Check if there are any changes and only commit if there are
        result = await targetRepo.ExecuteGitCommand(["diff-index", "--quiet", "--cached", "HEAD", "--"], cancellationToken);

        if (result.ExitCode == 0)
        {
            // When no changes happened, we disregard the work branch and return back to the target branch
            await targetRepo.CheckoutAsync(codeflow.HeadBranch);
            return false;
        }

        var commitMessage = await CommitBackflow(currentFlow, targetRepo, codeflow.Build, cancellationToken);

        await MergeWorkBranchAsync(
            codeflow,
            currentFlow,
            targetRepo,
            workBranch,
            headBranchExisted,
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
        if (lastFlow is not ForwardFlow ff || lastBackFlow == null)
        {
            return null;
        }

        return await repo.IsAncestorCommit(ff.RepoSha, lastBackFlow.RepoSha)
            ? lastForwardFlow
            : null;
    }

    protected async Task<(bool, SourceMapping, LastFlows, ILocalGitRepo)> PrepareVmrAndRepo(
        string mappingName,
        Build build,
        string targetBranch,
        string headBranch,
        NativePath? targetRepoPath,
        bool enableRebase,
        CancellationToken cancellationToken)
    {
        await _vmrCloneManager.PrepareVmrAsync([build.GetRepository()], [build.Commit], build.Commit, ShouldResetClones, cancellationToken);

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ISourceComponent repoInfo = _sourceManifest.GetRepoVersion(mappingName);

        var remotes = new[] { mapping.DefaultRemote, repoInfo.RemoteUri }
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToArray();

        ILocalGitRepo targetRepo;

        // Try to see if both base and target branch are available
        try
        {
            if (targetRepoPath == null)
            {
                targetRepo = await _repositoryCloneManager.PrepareCloneAsync(
                    mapping,
                    remotes,
                    [targetBranch, headBranch],
                    headBranch,
                    ShouldResetClones,
                    cancellationToken);
            }
            else
            {
                targetRepo = await _repositoryCloneManager.PrepareCloneAsync(
                    targetRepoPath,
                    remotes,
                    [targetBranch, headBranch],
                    headBranch,
                    ShouldResetClones,
                    cancellationToken);
            }

            LastFlows lastFlows = await GetLastFlowsAsync(mapping.Name, targetRepo, currentIsBackflow: true);
            return (true, mapping, lastFlows, targetRepo);
        }
        catch (NotFoundException)
        {
            // If target branch does not exist, we create it off of the base branch
            try
            {
                if (targetRepoPath == null)
                {
                    targetRepo = await _repositoryCloneManager.PrepareCloneAsync(
                        mapping,
                        remotes,
                        [targetBranch],
                        targetBranch,
                        ShouldResetClones,
                        cancellationToken);
                }
                else
                {
                    targetRepo = await _repositoryCloneManager.PrepareCloneAsync(
                        targetRepoPath,
                        remotes,
                        [targetBranch],
                        targetBranch,
                        ShouldResetClones,
                        cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to find branch {branch} in {uri}", targetBranch, string.Join(", ", remotes));
                throw new TargetBranchNotFoundException($"Failed to find target branch {targetBranch} in {string.Join(", ", remotes)}", e);
            }

            LastFlows lastFlows = await GetLastFlowsAsync(mapping.Name, targetRepo, currentIsBackflow: true);

            // Rebase strategy works on top of the target branch, non-rebase starts from the last point of synchronization
            if (!enableRebase)
            {
                await targetRepo.CheckoutAsync(lastFlows.LastFlow.RepoSha);
            }

            await targetRepo.CreateBranchAsync(headBranch, overwriteExistingBranch: true);

            return (false, mapping, lastFlows, targetRepo);
        }
    }

    private IReadOnlyCollection<string> GetPatchExclusions(SourceMapping mapping)
    {
        // Exclude all submodules that belong to the mapping
        var exclusions = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1));

        // Exclude version files as those will be handled manually
        exclusions = exclusions
            .Concat(DependencyFileManager.CodeflowDependencyFiles);

        // Exclude eng/common for non-arcade mappings (it will be copied separately based on the Arcade.Sdk package version)
        if (mapping.Name != VmrInfo.ArcadeMappingName)
        {
            exclusions = exclusions
                .Append(Constants.CommonScriptFilesPath);
        }

        return [.. exclusions.Select(VmrPatchHandler.GetExclusionRule)];
    }

    private NativePath GetPatchName(SourceMapping mapping, LastFlows lastFlows, Codeflow currentFlow)
        => _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlows.LastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";

    /// <summary>
    /// Traverses the current branch's history to find {depth}-th last backflow and creates a branch there.
    /// </summary>
    /// <returns>The {depth}-th last flow and its previous flows.</returns>
    protected override async Task<(Codeflow, LastFlows)> RewindToPreviousFlowAsync(
        SourceMapping mapping,
        ILocalGitRepo targetRepo,
        int depth,
        LastFlows previousFlows,
        string branchToCreate,
        string targetBranch,
        CancellationToken cancellationToken)
    {
        await targetRepo.ResetWorkingTree();
        await targetRepo.CheckoutAsync(targetBranch);

        Backflow previousFlow = previousFlows.LastBackFlow
            ?? throw new DarcException("No more backflows found to recreate");

        for (int i = 1; i < depth; i++)
        {
            var previousFlowSha = await _localGitClient.BlameLineAsync(
                targetRepo.Path / VersionFiles.VersionDetailsXml,
                line => line.Contains(VersionDetailsParser.SourceElementName) && line.Contains(previousFlow.VmrSha),
                previousFlow.RepoSha);

            await targetRepo.ResetWorkingTree();
            await targetRepo.CheckoutAsync(previousFlowSha);
            await _vmrCloneManager.PrepareVmrAsync(
                [_vmrInfo.VmrUri],
                [previousFlow.VmrSha],
                previousFlow.VmrSha,
                resetToRemote: false,
                cancellationToken);

            previousFlows = await GetLastFlowsAsync(mapping.Name, targetRepo, currentIsBackflow: true);
            previousFlow = previousFlows.LastBackFlow
                ?? throw new DarcException($"No more backflows found to recreate from {previousFlowSha}");
        }

        // Check out the repo before the flows we want to recreate
        await targetRepo.CheckoutAsync(previousFlow.RepoSha);
        await targetRepo.CreateBranchAsync(branchToCreate, overwriteExistingBranch: true);

        return (previousFlow, previousFlows);
    }

    protected override async Task EnsureCodeflowLinearityAsync(ILocalGitRepo repo, Codeflow currentFlow, LastFlows lastFlows)
    {
        var lastBackFlowVmrSha = lastFlows.LastBackFlow?.VmrSha;

        if (lastBackFlowVmrSha == null)
        {
            return;
        }

        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        if (!await vmr.IsAncestorCommit(lastBackFlowVmrSha, currentFlow.VmrSha))
        {
            throw new NonLinearCodeflowException(currentFlow.VmrSha, lastBackFlowVmrSha);
        }
    }

    private static async Task<string> CommitBackflow(Codeflow currentFlow, ILocalGitRepo targetRepo, Build build, CancellationToken cancellationToken)
    {
        var commitMessage = $"""
            Backflow from {build.GetRepository()} / {Commit.GetShortSha(currentFlow.VmrSha)} build {build.Id}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await targetRepo.CommitAsync(commitMessage, allowEmpty: false, cancellationToken: cancellationToken);
        await targetRepo.ResetWorkingTree();
        return commitMessage;
    }

    protected override NativePath GetEngCommonPath(NativePath sourceRepo) => sourceRepo / VmrInfo.ArcadeRepoDir / Constants.CommonScriptFilesPath;
    protected override bool TargetRepoIsVmr() => false;
    // During backflow, we're flowing a specific VMR commit that the build was built from, so we should just check it out
    protected virtual bool ShouldResetClones => false;
}
