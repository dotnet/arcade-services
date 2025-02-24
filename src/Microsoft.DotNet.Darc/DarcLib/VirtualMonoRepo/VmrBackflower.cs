// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrBackFlower
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
    /// <param name="discardPatches">Keep patch files?</param>
    Task<bool> FlowBackAsync(
        string mapping,
        NativePath targetRepo,
        Build buildToFlow,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);
}

internal class VmrBackFlower : VmrCodeFlower, IVmrBackFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly IRepositoryCloneManager _repositoryCloneManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IVersionFileConflictResolver _versionFileConflictResolver;
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
            IVersionFileConflictResolver versionFileConflictResolver,
            IFileSystem fileSystem,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, localGitRepoFactory, versionDetailsParser, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _vmrCloneManager = vmrCloneManager;
        _repositoryCloneManager = repositoryCloneManager;
        _localGitRepoFactory = localGitRepoFactory;
        _vmrPatchHandler = vmrPatchHandler;
        _workBranchFactory = workBranchFactory;
        _versionFileConflictResolver = versionFileConflictResolver;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<bool> FlowBackAsync(
        string mappingName,
        NativePath targetRepoPath,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
    {
        var targetRepo = _localGitRepoFactory.Create(targetRepoPath);
        (bool headBranchExisted, SourceMapping mapping) = await PrepareVmrAndRepo(
            mappingName,
            targetRepo,
            build,
            targetBranch,
            headBranch,
            cancellationToken);

        Codeflow lastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

        return await FlowBackAsync(
            mapping,
            targetRepo,
            lastFlow,
            build,
            excludedAssets,
            targetBranch,
            headBranch,
            discardPatches,
            headBranchExisted,
            cancellationToken);
    }

    protected async Task<bool> FlowBackAsync(
        SourceMapping mapping,
        ILocalGitRepo targetRepo,
        Codeflow lastFlow,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var currentFlow = new Backflow(build.Commit, lastFlow.RepoSha);
        var hasChanges = await FlowCodeAsync(
            lastFlow,
            currentFlow,
            targetRepo,
            mapping,
            build,
            excludedAssets,
            targetBranch,
            headBranch,
            discardPatches,
            headBranchExisted,
            cancellationToken);

        // We try to merge the target branch and we apply dependency updates
        var dependencyUpdates = await TryMergingBranch(
            mapping,
            lastFlow,
            currentFlow,
            targetRepo,
            build,
            headBranch,
            targetBranch,
            excludedAssets,
            cancellationToken);

        return hasChanges || dependencyUpdates.Any();
    }

    protected override async Task<bool> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        string newBranchName = currentFlow.GetBranchName();
        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";

        // When flowing from the VMR, ignore all submodules
        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            lastFlow.VmrSha,
            currentFlow.VmrSha,
            path: null,
            filters: GetPatchExclusions(mapping),
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            applicationPath: null,
            cancellationToken);

        if (patches.Count == 0 || patches.All(p => _fileSystem.GetFileInfo(p.Path).Length == 0))
        {
            _logger.LogInformation("There are no new changes for VMR between {sha1} and {sha2}",
                lastFlow.VmrSha,
                currentFlow.VmrSha);

            if (discardPatches)
            {
                foreach (VmrIngestionPatch patch in patches)
                {
                    _fileSystem.DeleteFile(patch.Path);
                }
            }

            return false;
        }

        _logger.LogInformation("Created {count} patch(es)", patches.Count);

        var workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, newBranchName, headBranch);

        try
        {
            foreach (VmrIngestionPatch patch in patches)
            {
                await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, reverseApply: false, cancellationToken);
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
                throw new ConflictInPrBranchException(e.Result, targetBranch, isForwardFlow: false);
            }

            // Otherwise, we have a conflicting change in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#conflicts
            _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating the previous flow..");

            // Find the last target commit in the repo
            var previousRepoSha = await BlameLineAsync(
                targetRepo.Path / VersionFiles.VersionDetailsXml,
                line => line.Contains(VersionDetailsParser.SourceElementName) && line.Contains(lastFlow.SourceSha),
                lastFlow.RepoSha);
            await targetRepo.CheckoutAsync(previousRepoSha);
            await targetRepo.CreateBranchAsync(headBranch, overwriteExistingBranch: true);

            // Reconstruct the previous flow's branch
            var lastLastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

            await FlowCodeAsync(
                lastLastFlow,
                lastFlow,
                targetRepo,
                mapping,
                // TODO (https://github.com/dotnet/arcade-services/issues/4166): Find a previous build?
                new Build(-1, DateTimeOffset.Now, 0, false, false, lastLastFlow.VmrSha, [], [], [], []),
                excludedAssets,
                headBranch,
                headBranch,
                discardPatches,
                headBranchExisted,
                cancellationToken);

            // The recursive call right above would returned checked out at targetBranch
            // The original work branch from above is no longer relevant. We need to create it again
            workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, newBranchName, headBranch);

            // The current patches should apply now
            foreach (VmrIngestionPatch patch in patches)
            {
                // TODO https://github.com/dotnet/arcade-services/issues/2995: Catch exceptions?
                await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, reverseApply: false, cancellationToken);
            }
        }

        var commitMessage = $"""
            [VMR] Codeflow {Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.VmrSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await targetRepo.CommitAsync(commitMessage, allowEmpty: false, cancellationToken: cancellationToken);
        await targetRepo.ResetWorkingTree();
        await workBranch.MergeBackAsync(commitMessage);

        _logger.LogInformation("Branch {branch} with code changes is ready in {repoDir}", headBranch, targetRepo);

        return true;
    }

    protected override async Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build build,
        string targetBranch,
        string headBranch,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        await targetRepo.CheckoutAsync(lastFlow.RepoSha);

        // If the target branch did not exist, we need to make sure it is created in the right location
        if (!headBranchExisted)
        {
            await targetRepo.CreateBranchAsync(headBranch, true);
        }

        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";
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
            // TODO https://github.com/dotnet/arcade-services/issues/2995: Handle exceptions
            await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, reverseApply: false, cancellationToken);
        }

        // TODO https://github.com/dotnet/arcade-services/issues/2995: Check if there are any changes and only commit if there are
        result = await targetRepo.ExecuteGitCommand(["diff-index", "--quiet", "--cached", "HEAD", "--"], cancellationToken);

        if (result.ExitCode == 0)
        {
            // TODO https://github.com/dotnet/arcade-services/issues/2995: Handle + clean up the work branch
            // When no changes happened, we disregard the work branch and return back to the target branch
            await targetRepo.CheckoutAsync(headBranch);
            return false;
        }

        var commitMessage = $"""
            [VMR] Codeflow {Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await targetRepo.CommitAsync(commitMessage, false, cancellationToken: cancellationToken);
        await targetRepo.ResetWorkingTree();
        await workBranch.MergeBackAsync(commitMessage);

        return true;
    }

    /// <summary>
    /// Tries to resolve well-known conflicts that can occur during a code flow operation.
    /// The conflicts can happen when backward a forward flow PRs get merged out of order.
    /// This can be shown on the following schema (the order of events is numbered):
    /// 
    ///     repo                   VMR
    ///       O────────────────────►O
    ///       │  2.                 │ 1.
    ///       │   O◄────────────────O- - ┐
    ///       │   │            4.   │
    ///     3.O───┼────────────►O   │    │
    ///       │   │             │   │
    ///       │ ┌─┘             │   │    │
    ///       │ │               │   │
    ///     5.O◄┘               └──►O 6. │
    ///       │                 7.  │    O (actual branch for 7. is based on top of 1.)
    ///       |────────────────►O   │
    ///       │                 └──►O 8.
    ///       │                     │
    ///
    /// The conflict arises in step 8. and is caused by the fact that:
    ///   - When the forward flow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
    ///   - This means that the PR branch will be based on 1. (the real PR branch is the "actual 7.")
    ///   - This means that when 6. merged, VMR's source-manifest.json got updated with the SHA of the 3.
    ///   - So the source-manifest in 6. contains the SHA of 3.
    ///   - The forward flow PR branch contains the SHA of 5.
    ///   - So the source-manifest file conflicts on the SHA (3. vs 5.)
    ///   - There's also a similar conflict in the git-info files.
    ///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
    ///   - This is because basically we know we want to set the version files to point at 5.
    /// </summary>
    private async Task<List<DependencyUpdate>> TryMergingBranch(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo repo,
        Build build,
        string headBranch,
        string branchToMerge,
        IReadOnlyCollection<string>? excludedAssets,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking if target branch {targetBranch} has conflicts with {headBranch}", branchToMerge, headBranch);

        await repo.CheckoutAsync(headBranch);
        var result = await repo.RunGitCommandAsync(["merge", "--no-commit", "--no-ff", branchToMerge], cancellationToken);
        if (result.Succeeded)
        {
            try
            {
                await repo.CommitAsync(
                    $"Merging {branchToMerge} into {headBranch}",
                    allowEmpty: false,
                    cancellationToken: CancellationToken.None);

                _logger.LogInformation("Successfully merged the branch {targetBranch} into {headBranch} in {repoPath}",
                    branchToMerge,
                    headBranch,
                    repo.Path);
            }
            catch (Exception e) when (e.Message.Contains("nothing to commit"))
            {
                // Our branch might be fast-forward and so no commit was needed
            }

            try
            {
                // After a successful merge, we update dependencies
                return await _versionFileConflictResolver.BackflowDependenciesAndToolset(
                    mapping.Name,
                    repo,
                    branchToMerge,
                    build,
                    excludedAssets,
                    lastFlow,
                    (Backflow)currentFlow,
                    cancellationToken);
            }
            catch (Exception e)
            {
                // We don't want to push this as there is some problem
                _logger.LogError(e, "Failed to update dependencies after merging {targetBranch} into {headBranch} in {repoPath}",
                    branchToMerge,
                    headBranch,
                    repo.Path);
                throw;
            }
        }

        async Task AbortMerge()
        {
            var result = await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
            result.ThrowIfFailed("Failed to abort the merge");
        }

        // When we had conflicts, we verify that they can be resolved (i.e. they are only in version files)
        result = await repo.RunGitCommandAsync(["diff", "--name-only", "--diff-filter=U", "--relative"], cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogInformation("Failed to merge the branch {targetBranch} into {headBranch} in {repoPath}",
                branchToMerge,
                headBranch,
                repo.Path);
            await AbortMerge();
            return [];
        }

        var conflictedFiles = result.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim());

        var unresolvableConflicts = conflictedFiles
            .Except(DependencyFileManager.DependencyFiles)
            .ToList();

        if (unresolvableConflicts.Count > 0)
        {
            _logger.LogInformation("Failed to merge the branch {targetBranch} into {headBranch} due to unresolvable conflicts: {conflicts}",
                branchToMerge,
                headBranch,
                string.Join(", ", unresolvableConflicts));

            await AbortMerge();
            return [];
        }

        foreach (var file in conflictedFiles)
        {
            // Revert files to our version so that we can resolve the conflicts
            await repo.RunGitCommandAsync(["checkout", "--theirs", file], cancellationToken);
        }

        try
        {
            // When only version files are conflicted, we can resolve the conflicts by generating them correctly
            return await _versionFileConflictResolver.BackflowDependenciesAndToolset(
                mapping.Name,
                repo,
                branchToMerge,
                build,
                excludedAssets,
                lastFlow,
                (Backflow)currentFlow,
                cancellationToken);
        }
        catch (Exception e)
        {
            // We don't want to push this as there is some problem
            _logger.LogError(e, "Failed to update dependencies after merging {targetBranch} into {headBranch} in {repoPath}",
                branchToMerge,
                headBranch,
                repo.Path);
            return [];
        }
    }

    private async Task<(bool, SourceMapping)> PrepareVmrAndRepo(
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
            await _repositoryCloneManager.PrepareCloneAsync(
                mapping,
                remotes,
                [targetBranch, headBranch],
                headBranch,
                ShouldResetVmr,
                cancellationToken);
            return (true, mapping);
        }
        catch (NotFoundException)
        {
            // If target branch does not exist, we create it off of the base branch
            await targetRepo.CheckoutAsync(targetBranch);
            await targetRepo.CreateBranchAsync(headBranch);
            return (false, mapping);
        };
    }

    private IReadOnlyCollection<string> GetPatchExclusions(SourceMapping mapping) =>
        // Exclude all submodules that belong to the mapping
        [.._sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1))

        // Exclude version files as those will be handled manually
        .Concat(DependencyFileManager.DependencyFiles)

        // Exclude eng/common as that will be copied based on the arcade version
        .Append(Constants.CommonScriptFilesPath)
        .Select(VmrPatchHandler.GetExclusionRule)];

    protected override NativePath GetEngCommonPath(NativePath sourceRepo) => sourceRepo / VmrInfo.ArcadeRepoDir / Constants.CommonScriptFilesPath;
    protected override bool TargetRepoIsVmr() => false;
    // During backflow, we're flowing a specific VMR commit that the build was built from, so we should just check it out
    protected virtual bool ShouldResetVmr => false;
}
