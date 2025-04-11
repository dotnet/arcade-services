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
    /// <param name="discardPatches">Keep patch files?</param>
    /// <param name="headBranchExisted">Whether the PR branch already exists in the VMR. Null when we don't as the VMR needs to be prepared</param>
    /// <returns>True when there were changes to be flown</returns>
    /// <returns>CodeFlowResult containing information about the codeflow calculation</returns>
    Task<CodeFlowResult> FlowForwardAsync(
        string mapping,
        NativePath sourceRepo,
        Build buildToFlow,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        string targetVmrUri,
        bool discardPatches = false,
        bool? headBranchExisted = null,
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
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly IBasicBarClient _barClient;
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
            IProcessManager processManager,
            IFileSystem fileSystem,
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
        _processManager = processManager;
        _fileSystem = fileSystem;
        _barClient = barClient;
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
        bool discardPatches = false,
        bool? headBranchExisted = null,
        CancellationToken cancellationToken = default)
    {
        // Null means, we don't know and we need to clone the VMR
        if (!headBranchExisted.HasValue)
        {
            headBranchExisted = await PrepareVmr(targetVmrUri, targetBranch, headBranch, cancellationToken);
        }

        ILocalGitRepo sourceRepo = _localGitRepoFactory.Create(repoPath);
        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ISourceComponent repoInfo = _sourceManifest.GetRepoVersion(mapping.Name);

        await sourceRepo.FetchAllAsync([mapping.DefaultRemote, repoInfo.RemoteUri], cancellationToken);
        await sourceRepo.CheckoutAsync(build.Commit);

        Codeflow lastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: false);
        ForwardFlow currentFlow = new(lastFlow.TargetSha, build.Commit);

        bool hasChanges = await FlowCodeAsync(
            lastFlow,
            currentFlow,
            sourceRepo,
            mapping,
            build,
            excludedAssets,
            targetBranch,
            headBranch,
            discardPatches,
            headBranchExisted.Value,
            cancellationToken);

        IReadOnlyCollection<UnixPath>? conflictedFiles = null;
        if (hasChanges)
        {
            // We try to merge the target branch so that we can potentially
            // resolve some expected conflicts in the version files
            ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
            conflictedFiles = await TryMergingBranch(
                mapping.Name,
                vmr,
                build,
                excludedAssets,
                headBranch,
                targetBranch,
                cancellationToken);
        }

        return new CodeFlowResult(
            hasChanges,
            conflictedFiles, 
            sourceRepo.Path,
            lastFlow,
            DependencyUpdates: []);
    }

    protected async Task<bool> PrepareVmr(
        string vmrUri,
        string targetBranch,
        string prBranch,
        CancellationToken cancellationToken)
    {
        _vmrInfo.VmrUri = vmrUri;
        try
        {
            await _vmrCloneManager.PrepareVmrAsync(
                [vmrUri],
                [targetBranch, prBranch],
                prBranch,
                ShouldResetVmr,
                cancellationToken);
            return true;
        }
        catch (NotFoundException)
        {
            // This means the target branch does not exist yet
            // We will create it off of the base branch
            var vmr = await _vmrCloneManager.PrepareVmrAsync(
                [vmrUri],
                [targetBranch],
                targetBranch,
                ShouldResetVmr,
                cancellationToken);

            await vmr.CreateBranchAsync(prBranch);
            return false;
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
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        bool hadUpdates;

        try
        {
            // If the build produced any assets, we use the number to update VMR's git info files
            // The git info files won't be important by then and probably removed but let's keep it for now
            string? targetVersion = build.Assets.FirstOrDefault()?.Version;

            hadUpdates = await _vmrUpdater.UpdateRepository(
                mapping,
                build,
                resetToRemoteWhenCloningRepo: ShouldResetClones,
                cancellationToken);
        }
        catch (PatchApplicationFailedException e)
        {
            // When we are updating an already existing PR branch, there can be conflicting changes in the PR from devs.
            if (headBranchExisted)
            {
                _logger.LogInformation("Failed to update a PR branch because of a conflict. Stopping the flow..");
                throw new ConflictInPrBranchException(e.Result, targetBranch, mapping.Name, isForwardFlow: true);
            }

            // This happens when a conflicting change was made in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#conflicts
            hadUpdates = await RecreatePreviousFlowAndApplyBuild(
                mapping,
                lastFlow,
                headBranch,
                sourceRepo,
                excludedAssets,
                targetBranch,
                build,
                discardPatches,
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
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        await sourceRepo.CheckoutAsync(lastFlow.RepoSha);

        var patchName = _vmrInfo.TmpPath / $"{headBranch.Replace('/', '-')}.patch";
        var branchName = currentFlow.GetBranchName();

        List<GitSubmoduleInfo> submodules =
        [
            .. await sourceRepo.GetGitSubmodulesAsync(lastFlow.RepoSha),
            .. await sourceRepo.GetGitSubmodulesAsync(currentFlow.RepoSha),
        ];

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to the VMR, we remove all files but submodules and cloaked files
        List<string> removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. submodules.Select(s => s.Path).Distinct().Select(VmrPatchHandler.GetExclusionRule),
        ];

        var result = await _processManager.Execute(
            _processManager.GitExecutable,
            ["rm", "-r", "-q", "--", .. removalFilters],
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            cancellationToken: cancellationToken);

        result.ThrowIfFailed($"Failed to remove files from {sourceRepo}");

        // We make the VMR believe it has the zero commit of the repo as it matches the dir/git state at the moment
        _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
            mapping,
            build.GetRepository(),
            Constants.EmptyGitObject,
            _dependencyTracker.GetDependencyVersion(mapping)!.PackageVersion,
            Parent: null,
            build.AzureDevOpsBuildNumber,
            build.Id));

        // TODO https://github.com/dotnet/arcade-services/issues/4178: Detect if no changes.
        // Technically, if we only changed metadata files, there are no updates still
        return await _vmrUpdater.UpdateRepository(
            mapping,
            build,
            resetToRemoteWhenCloningRepo: ShouldResetClones,
            cancellationToken);
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
    /// <returns>Conflicted files (if any)</returns>
    private async Task<IReadOnlyCollection<UnixPath>> TryMergingBranch(
        string mappingName,
        ILocalGitRepo repo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string branchToMerge,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking if target branch {targetBranch} has conflicts with {headBranch}", branchToMerge, targetBranch);

        await repo.CheckoutAsync(targetBranch);
        var result = await repo.RunGitCommandAsync(["merge", "--no-commit", "--no-ff", branchToMerge], cancellationToken);
        if (result.Succeeded)
        {
            try
            {
                await repo.CommitAsync(
                    $"Merge {branchToMerge} into {targetBranch}",
                    allowEmpty: false,
                    cancellationToken: CancellationToken.None);

                _logger.LogInformation("Successfully merged the branch {targetBranch} into {headBranch} in {repoPath}",
                    branchToMerge,
                    targetBranch,
                    repo.Path);
            }
            catch (Exception e) when (e.Message.Contains("nothing to commit"))
            {
                // Our branch might be fast-forward and so no commit is needed
                _logger.LogInformation("Branch {targetBranch} had no updates since it was last merged into {headBranch}",
                    branchToMerge,
                    targetBranch);
            }

            return [];
        }
        else
        {
            result = await repo.RunGitCommandAsync(["diff", "--name-only", "--diff-filter=U", "--relative"], cancellationToken);
            if (!result.Succeeded)
            {
                var abort = await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
                abort.ThrowIfFailed("Failed to abort a merge when resolving version file conflicts");
                result.ThrowIfFailed("Failed to resolve version file conflicts - failed to get a list of conflicted files");
                throw new InvalidOperationException(); // the line above will throw, including more details
            }

            var conflictedFiles = result.StandardOutput
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => new UnixPath(line.Trim()))
                .ToList();

            UnixPath[] allowedConflicts = [
                // source-manifest.json
                VmrInfo.DefaultRelativeSourceManifestPath,

            // git-info for the repo
            new UnixPath($"{VmrInfo.GitInfoSourcesDir}/{mappingName}.props")
            ];

            var unresolvableConflicts = conflictedFiles
                .Except(allowedConflicts)
                .ToList();

            if (unresolvableConflicts.Count > 0)
            {
                _logger.LogInformation("Failed to merge the branch {targetBranch} into {headBranch} due to unresolvable conflicts: {conflicts}",
                    branchToMerge,
                    targetBranch,
                    string.Join(", ", unresolvableConflicts));

                result = await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
                return conflictedFiles;
            }

            if (!await TryResolveConflicts(
                mappingName,
                repo,
                build,
                excludedAssets,
                targetBranch,
                conflictedFiles,
                cancellationToken))
            {
                return conflictedFiles;
            }

            _logger.LogInformation("Successfully resolved file conflicts between branches {targetBranch} and {headBranch}",
                branchToMerge,
                targetBranch);

            try
            {
                await repo.CommitAsync(
                    $"Merge branch {branchToMerge} into {targetBranch}",
                    allowEmpty: false,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception e) when (e.Message.Contains("Your branch is ahead of"))
            {
                // There was no reason to merge, we're fast-forward ahead from the target branch
            }

            return [];
        }
    }

    protected virtual async Task<bool> TryResolveConflicts(
        string mappingName,
        ILocalGitRepo repo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        IEnumerable<UnixPath> conflictedFiles,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in conflictedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await TryResolvingConflict(mappingName, repo, filePath, cancellationToken))
                {
                    continue;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to resolve conflicts in {filePath}", filePath);
            }

            await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
            return false;
        }

        return true;
    }

    private async Task<bool> TryResolvingConflict(
        string mappingName,
        ILocalGitRepo repo,
        string filePath,
        CancellationToken cancellationToken)
    {
        // Known conflict in source-manifest.json
        if (string.Equals(filePath, VmrInfo.DefaultRelativeSourceManifestPath, StringComparison.OrdinalIgnoreCase))
        {
            await TryResolvingSourceManifestConflict(repo, mappingName!, cancellationToken);
            return true;
        }

        // Git-info file conflict
        _logger.LogInformation("Auto-resolving conflict in {file} using PR version", filePath);
        await repo.RunGitCommandAsync(["checkout", "--ours", filePath], cancellationToken);
        await repo.StageAsync([filePath], cancellationToken);
        return true;
    }

    // TODO https://github.com/dotnet/arcade-services/issues/3378: This might not work for batched subscriptions
    private async Task TryResolvingSourceManifestConflict(ILocalGitRepo vmr, string mappingName, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Auto-resolving conflict in {file}", VmrInfo.DefaultRelativeSourceManifestPath);

        // We load the source manifest from the target branch and replace the
        // current mapping (and its submodules) with our branches' information
        var result = await vmr.RunGitCommandAsync(
            ["show", "MERGE_HEAD:" + VmrInfo.DefaultRelativeSourceManifestPath],
            cancellationToken);

        var theirSourceManifest = SourceManifest.FromFile(result.StandardOutput);
        var ourSourceManifest = _sourceManifest;
        var updatedMapping = ourSourceManifest.Repositories.First(r => r.Path == mappingName);

        theirSourceManifest.UpdateVersion(
            mappingName,
            updatedMapping.RemoteUri,
            updatedMapping.CommitSha,
            updatedMapping.PackageVersion,
            updatedMapping.BarId);

        foreach (var submodule in theirSourceManifest.Submodules.Where(s => s.Path.StartsWith(mappingName + "/")))
        {
            theirSourceManifest.RemoveSubmodule(submodule);
        }

        foreach (var submodule in _sourceManifest.Submodules.Where(s => s.Path.StartsWith(mappingName + "/")))
        {
            theirSourceManifest.UpdateSubmodule(submodule);
        }

        _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, theirSourceManifest.ToJson());
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);
        await vmr.StageAsync([_vmrInfo.SourceManifestPath], cancellationToken);
    }

    private async Task<bool> RecreatePreviousFlowAndApplyBuild(
        SourceMapping mapping,
        Codeflow lastFlow,
        string headBranch,
        ILocalGitRepo sourceRepo,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        Build build,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating the previous flow..");

        var lastLastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: true);

        // Find the BarID of the last flown repo build
        RepositoryRecord previouslyAppliedRepositoryRecord = _sourceManifest.GetRepositoryRecord(mapping.Name);
        Build previouslyAppliedBuild;
        if (previouslyAppliedRepositoryRecord.BarId == null)
        {
            // If we don't find the previously applied build, we'll just use the previously flown sha to recreate the flow
            // We'll apply a new build on top of this one, so the source manifest will get updated anyway
            previouslyAppliedBuild = new(-1, DateTimeOffset.Now, 0, false, false, lastLastFlow.SourceSha, [], [], [], []);
        }
        else
        {
            previouslyAppliedBuild = await _barClient.GetBuildAsync(previouslyAppliedRepositoryRecord.BarId.Value);
        }

        // Find the VMR sha before the last successful flow
        var previousFlowTargetSha = await _localGitClient.BlameLineAsync(
            _vmrInfo.SourceManifestPath,
            line => line.Contains(lastFlow.SourceSha),
            lastFlow.TargetSha);
        var vmr = await _vmrCloneManager.PrepareVmrAsync(
            [_vmrInfo.VmrUri],
            [previousFlowTargetSha],
            previousFlowTargetSha,
            resetToRemote: false,
            cancellationToken);
        await vmr.CreateBranchAsync(headBranch, overwriteExistingBranch: true);

        // Reconstruct the previous flow's branch
        await FlowCodeAsync(
            lastLastFlow,
            lastFlow,
            sourceRepo,
            mapping,
            previouslyAppliedBuild,
            excludedAssets,
            targetBranch,
            headBranch,
            discardPatches,
            headBranchExisted,
            cancellationToken);

        // We apply the current changes on top again - they should apply now
        try
        {
            return await _vmrUpdater.UpdateRepository(
                mapping,
                build,
                resetToRemoteWhenCloningRepo: ShouldResetClones,
                cancellationToken);
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
