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
    Task<CodeFlowResult> FlowBackAsync(
        string mapping,
        NativePath targetRepo,
        Build buildToFlow,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
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
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, localGitRepoFactory, versionDetailsParser, logger)
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
        CancellationToken cancellationToken = default)
    {
        var targetRepo = _localGitRepoFactory.Create(targetRepoPath);
        (bool headBranchExisted, SourceMapping mapping, LastFlows lastFlows) = await PrepareVmrAndRepo(
            mappingName,
            targetRepo,
            build,
            targetBranch,
            headBranch,
            cancellationToken);

        return await FlowBackAsync(
            mapping,
            targetRepo,
            lastFlows,
            build,
            excludedAssets,
            targetBranch,
            headBranch,
            headBranchExisted,
            cancellationToken);
    }

    protected async Task<CodeFlowResult> FlowBackAsync(
        SourceMapping mapping,
        ILocalGitRepo targetRepo,
        LastFlows lastFlows,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var currentFlow = new Backflow(build.Commit, lastFlows.LastFlow.RepoSha);
        var hasChanges = await FlowCodeAsync(
            lastFlows,
            currentFlow,
            targetRepo,
            mapping,
            build,
            excludedAssets,
            targetBranch,
            headBranch,
            headBranchExisted,
            cancellationToken);

        // We try to merge the target branch and we apply dependency updates
        VersionFileUpdateResult mergeResult = await _conflictResolver.TryMergingBranchAndUpdateDependencies(
            mapping,
            lastFlows.LastFlow,
            currentFlow,
            lastFlows.CrossingFlow,
            targetRepo,
            build,
            headBranch,
            targetBranch,
            excludedAssets,
            headBranchExisted,
            cancellationToken);

        return new CodeFlowResult(
            hasChanges || mergeResult.DependencyUpdates.Count > 0,
            mergeResult.ConflictedFiles,
            targetRepo.Path,
            mergeResult.DependencyUpdates);
    }

    protected override async Task<bool> SameDirectionFlowAsync(
        SourceMapping mapping,
        LastFlows lastFlows,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var lastFlownSha = lastFlows.LastFlow.VmrSha;
        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlownSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";

        // When flowing from the VMR, ignore all submodules
        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            lastFlownSha,
            currentFlow.VmrSha,
            path: null,
            filters: GetPatchExclusions(mapping),
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
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

        _logger.LogInformation("Created {count} patch(es)", patches.Count);

        string newBranchName = currentFlow.GetBranchName();
        IWorkBranch? workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, newBranchName, headBranch);

        try
        {
            foreach (VmrIngestionPatch patch in patches)
            {
                await _vmrPatchHandler.ApplyPatch(
                    patch,
                    targetRepo.Path,
                    removePatchAfter: true,
                    reverseApply: false,
                    cancellationToken);
            }
        }
        catch (PatchApplicationFailedException e)
        {
            _logger.LogInformation(e.Message);

            // When we are updating an already existing PR branch, there can be conflicting changes in the PR from devs.
            // In that case we want to throw as that is a conflict we don't want to try to resolve.
            if (headBranchExisted)
            {
                _logger.LogInformation("Failed to update a PR branch because of a conflict. Stopping the flow..");
                throw new ConflictInPrBranchException(e.Result.StandardError, targetBranch, mapping.Name, isForwardFlow: false);
            }

            // Otherwise, we have a conflicting change in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/dotnet/tree/main/docs/VMR-Full-Code-Flow.md#conflicts
            await RecreatePreviousFlowAndApplyChanges(
                mapping,
                build,
                targetRepo,
                lastFlows,
                headBranch,
                targetBranch,
                excludedAssets,
                patches,
                cancellationToken);

            // We no longer need the work branch as we recreated the previous flow in a new head branch directly
            workBranch = null;
        }

        await CommitAndMergeWorkBranch(
            mapping,
            build,
            currentFlow,
            targetRepo,
            targetBranch,
            headBranch,
            workBranch,
            cancellationToken);

        return true;
    }

    protected override async Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        LastFlows lastFlows,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build build,
        string targetBranch,
        string headBranch,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        if (!headBranchExisted)
        {
            // If the target branch did not exist, we need to make sure it is created in the right location
            await targetRepo.CheckoutAsync(lastFlows.LastFlow.RepoSha);
            await targetRepo.CreateBranchAsync(headBranch, true);
        }
        else
        {
            // If it did, we need to check out the last point of synchronization on it
            await targetRepo.CheckoutAsync(lastFlows.LastBackFlow!.RepoSha);
        }

        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlows.LastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";
        var branchName = currentFlow.GetBranchName();
        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName, headBranch);
        _logger.LogInformation("Created temporary branch {branchName} in {repoDir}", branchName, targetRepo);

        // We leave the inlined submodules in the VMR
        var exclusions = GetPatchExclusions(mapping);

        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            Constants.EmptyGitObject,
            currentFlow.VmrSha,
            path: null,
            filters: exclusions,
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            applicationPath: null,
            ignoreLineEndings: false,
            cancellationToken);

        _logger.LogInformation("Created {count} patch(es)", patches.Count);

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to a repo, we remove all repo files but submodules and cloaked files
        List<string> removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. exclusions,
        ];

        string[] args = ["rm", "-r", "-q"];
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
        foreach (var patch in patches)
        {
            await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, removePatchAfter: true, reverseApply: false, cancellationToken);
        }

        // Check if there are any changes and only commit if there are
        result = await targetRepo.ExecuteGitCommand(["diff-index", "--quiet", "--cached", "HEAD", "--"], cancellationToken);

        if (result.ExitCode == 0)
        {
            // When no changes happened, we disregard the work branch and return back to the target branch
            await targetRepo.CheckoutAsync(headBranch);
            return false;
        }

        await CommitAndMergeWorkBranch(
            mapping,
            build,
            currentFlow,
            targetRepo,
            targetBranch,
            headBranch,
            workBranch,
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

    private async Task<(bool, SourceMapping, LastFlows)> PrepareVmrAndRepo(
        string mappingName,
        ILocalGitRepo targetRepo,
        Build build,
        string targetBranch,
        string headBranch,
        CancellationToken cancellationToken)
    {
        await _vmrCloneManager.PrepareVmrAsync([build.GetRepository()], [build.Commit], build.Commit, ShouldResetVmr, cancellationToken);

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ISourceComponent repoInfo = _sourceManifest.GetRepoVersion(mappingName);

        var remotes = new[] { mapping.DefaultRemote, repoInfo.RemoteUri }
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToArray();

        // Refresh the repo
        await targetRepo.FetchAllAsync(remotes, cancellationToken);

        try
        {
            // Try to see if both base and target branch are available
            targetRepo = await _repositoryCloneManager.PrepareCloneAsync(
                mapping,
                remotes,
                [targetBranch, headBranch],
                headBranch,
                ShouldResetVmr,
                cancellationToken);

            LastFlows lastFlows = await GetLastFlowsAsync(mapping, targetRepo, currentIsBackflow: true);
            return (true, mapping, lastFlows);
        }
        catch (NotFoundException)
        {
            try
            {
                // If target branch does not exist, we create it off of the base branch
                targetRepo = await _repositoryCloneManager.PrepareCloneAsync(
                    mapping,
                    remotes,
                    [targetBranch],
                    targetBranch,
                    ShouldResetVmr,
                    cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to find branch {branch} in {uri}", targetBranch, string.Join(", ", remotes));
                throw new TargetBranchNotFoundException($"Failed to find target branch {targetBranch} in {string.Join(", ", remotes)}", e);
            }

            LastFlows lastFlows = await GetLastFlowsAsync(mapping, targetRepo, currentIsBackflow: true);
            await targetRepo.CheckoutAsync(lastFlows.LastFlow.RepoSha);
            await targetRepo.CreateBranchAsync(headBranch);
            return (false, mapping, lastFlows);
        }
    }

    private IReadOnlyCollection<string> GetPatchExclusions(SourceMapping mapping)
    {
        // Exclude all submodules that belong to the mapping
        var exclusions = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1));

        // Exclude version files as those will be handled manually
        // TODO: once all codeflow repos/branches add VersionDetailsProps, we should remove Version.props from this list
        // https://github.com/dotnet/arcade-services/issues/4998
        exclusions = exclusions
            .Concat(DependencyFileManager.DependencyFiles);

        // Exclude eng/common for non-arcade mappings (it will be copied separately based on the Arcade.Sdk package version)
        if (mapping.Name != VmrInfo.ArcadeMappingName)
        {
            exclusions = exclusions
                .Append(Constants.CommonScriptFilesPath);
        }

        return [.. exclusions.Select(VmrPatchHandler.GetExclusionRule)];
    }

    private async Task RecreatePreviousFlowAndApplyChanges(
        SourceMapping mapping,
        Build build,
        ILocalGitRepo targetRepo,
        LastFlows lastFlows,
        string headBranch,
        string targetBranch,
        IReadOnlyCollection<string>? excludedAssets,
        List<VmrIngestionPatch> patches,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating previous flows..");

        // We recursively try to re-create previous flows until we find the one that introduced the conflict with the current flown
        int flowsToRecreate = 1;
        while (flowsToRecreate < 100)
        {
            _logger.LogInformation("Trying to recreate {count} previous flow(s)..", flowsToRecreate);

            Backflow deepestFlowToRecreate = lastFlows.LastBackFlow
                    ?? throw new DarcException("No more backflows found to recreate");
            LastFlows previousFlows = lastFlows;

            for (int i = 1; i < flowsToRecreate; i++)
            {
                var previousFlowRepoSha = await _localGitClient.BlameLineAsync(
                    targetRepo.Path / VersionFiles.VersionDetailsXml,
                    line => line.Contains(VersionDetailsParser.SourceElementName) && line.Contains(deepestFlowToRecreate.VmrSha),
                    deepestFlowToRecreate.RepoSha);

                await targetRepo.ResetWorkingTree();
                await targetRepo.CheckoutAsync(previousFlowRepoSha);
                await _vmrCloneManager.PrepareVmrAsync(
                    [_vmrInfo.VmrUri],
                    [deepestFlowToRecreate.VmrSha],
                    deepestFlowToRecreate.VmrSha,
                    resetToRemote: false,
                    cancellationToken);

                previousFlows = await GetLastFlowsAsync(mapping, targetRepo, currentIsBackflow: true);
                deepestFlowToRecreate = previousFlows.LastBackFlow
                    ?? throw new DarcException($"No more backflows found to recreate from {previousFlowRepoSha}");
            }

            // Check out the repo before the flows we want to recreate
            await targetRepo.CheckoutAsync(deepestFlowToRecreate.RepoSha);
            await targetRepo.CreateBranchAsync(headBranch, overwriteExistingBranch: true);

            // Create a fake previously applied build. We only care about the sha here, because it will get overwritten anyway
            Build previouslyAppliedBuild = new(-1, DateTimeOffset.Now, 0, false, false, lastFlows.LastBackFlow.VmrSha, [], [], [], [])
            {
                GitHubRepository = build.GitHubRepository,
                AzureDevOpsRepository = build.AzureDevOpsRepository
            };

            // We reconstruct the previous flow's branch
            await FlowCodeAsync(
                previousFlows,
                new Backflow(previouslyAppliedBuild.Commit, deepestFlowToRecreate.RepoSha),
                targetRepo,
                mapping,
                previouslyAppliedBuild,
                excludedAssets,
                targetBranch,
                headBranch,
                headBranchExisted: false,
                cancellationToken);

            // We apply the current changes on top again to check if they apply now
            try
            {
                // The current patches should apply now
                foreach (VmrIngestionPatch patch in patches)
                {
                    await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, removePatchAfter: true, reverseApply: false, cancellationToken);
                }

                _logger.LogInformation("Successfully recreated {count} flows and applied new changes from {sha}",
                    flowsToRecreate,
                    build.Commit);

                return;
            }
            catch (PatchApplicationFailedException)
            {
                _logger.LogInformation("Recreated {count} flows but conflict with a previous flow still exists. Recreating deeper...", flowsToRecreate);
                flowsToRecreate++;
                await targetRepo.ResetWorkingTree();
                await targetRepo.CheckoutAsync(targetBranch);
                continue;
            }
            catch (Exception e)
            {
                _logger.LogCritical("Failed to apply changes on top of previously recreated code flow: {message}", e.Message);
                throw;
            }
        }

        throw new DarcException($"Failed to apply changes due to conflicts even after {flowsToRecreate} previous flows were recreated");
    }

    private async Task CommitAndMergeWorkBranch(
        SourceMapping mapping,
        Build build,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        string targetBranch,
        string headBranch,
        IWorkBranch? workBranch,
        CancellationToken cancellationToken)
    {
        var commitMessage = $"""
            Backflow from {build.GetRepository()} / {Commit.GetShortSha(currentFlow.VmrSha)} build {build.Id}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await targetRepo.CommitAsync(commitMessage, allowEmpty: false, cancellationToken: cancellationToken);
        await targetRepo.ResetWorkingTree();

        if (workBranch != null)
        {
            try
            {
                await workBranch.MergeBackAsync(commitMessage);
            }
            catch (WorkBranchInConflictException e)
            {
                _logger.LogInformation(e.Message);
                throw new ConflictInPrBranchException(e.ExecutionResult.StandardError, targetBranch, mapping.Name, isForwardFlow: false);
            }
        }

        _logger.LogInformation("Branch {branch} with code changes is ready in {repoDir}", headBranch, targetRepo);
    }

    protected override NativePath GetEngCommonPath(NativePath sourceRepo) => sourceRepo / VmrInfo.ArcadeRepoDir / Constants.CommonScriptFilesPath;
    protected override bool TargetRepoIsVmr() => false;
    // During backflow, we're flowing a specific VMR commit that the build was built from, so we should just check it out
    protected virtual bool ShouldResetVmr => false;
}
