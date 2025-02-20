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
public interface IVmrForwardFlower
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

internal class VmrForwardFlower : VmrCodeFlower, IVmrForwardFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly ICodeFlowVmrUpdater _vmrUpdater;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
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
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, localGitRepoFactory, versionDetailsParser, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _vmrUpdater = vmrUpdater;
        _dependencyTracker = dependencyTracker;
        _vmrCloneManager = vmrCloneManager;
        _localGitRepoFactory = localGitRepoFactory;
        _processManager = processManager;
        _fileSystem = fileSystem;
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

        bool hasChanges = await FlowCodeAsync(
            lastFlow,
            new ForwardFlow(lastFlow.TargetSha, build.Commit),
            sourceRepo,
            mapping,
            build,
            excludedAssets,
            targetBranch,
            headBranch,
            discardPatches,
            headBranchExisted.Value,
            cancellationToken);

        if (hasChanges)
        {
            // We try to merge the target branch so that we can potentially
            // resolve some expected conflicts in the version files
            ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
            await TryMergingBranch(
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
            sourceRepo.Path,
            lastFlow.RepoSha,
            lastFlow.VmrSha);
    }

    protected async Task<bool> PrepareVmr(
        string vmrUri,
        string targetBranch,
        string prBranch,
        CancellationToken cancellationToken)
    {
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
        string branchName = currentFlow.GetBranchName();

        List<AdditionalRemote> additionalRemotes =
        [
            new AdditionalRemote(mapping.Name, sourceRepo.Path),
            new AdditionalRemote(mapping.Name, build.GetRepository()),
        ];

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
                throw new ConflictInPrBranchException(e.Result, targetBranch, isForwardFlow: true);
            }

            // This happens when a conflicting change was made in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#conflicts
            _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating the previous flow..");

            // Find the last target commit in the repo
            var previousFlowTargetSha = await BlameLineAsync(
                _vmrInfo.SourceManifestPath,
                line => line.Contains(lastFlow.SourceSha),
                lastFlow.TargetSha);
            var vmr = await _vmrCloneManager.PrepareVmrAsync(
                [_vmrInfo.VmrUri],
                [previousFlowTargetSha],
                previousFlowTargetSha,
                ShouldResetVmr,
                cancellationToken);
            await vmr.CreateBranchAsync(headBranch, overwriteExistingBranch: true);

            // Reconstruct the previous flow's branch
            var lastLastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: true);
            await FlowCodeAsync(
                lastLastFlow,
                lastFlow,
                sourceRepo,
                mapping,
                // TODO (https://github.com/dotnet/arcade-services/issues/4166): Find a previous build?
                new Build(-1, DateTimeOffset.Now, 0, false, false, lastLastFlow.SourceSha, [], [], [], []),
                excludedAssets,
                targetBranch,
                headBranch,
                discardPatches,
                headBranchExisted,
                cancellationToken);

            // We apply the current changes on top again - they should apply now
            // TODO https://github.com/dotnet/arcade-services/issues/2995: Handle exceptions
            hadUpdates = await _vmrUpdater.UpdateRepository(
                mapping,
                build,
                resetToRemoteWhenCloningRepo: ShouldResetClones,
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
        CancellationToken cancellationToken)
    {
        await sourceRepo.CheckoutAsync(lastFlow.TargetSha);

        var patchName = _vmrInfo.TmpPath / $"{headBranch.Replace('/', '-')}.patch";
        var branchName = currentFlow.GetBranchName();

        List<GitSubmoduleInfo> submodules =
        [
            .. await sourceRepo.GetGitSubmodulesAsync(lastFlow.RepoSha),
            .. await sourceRepo.GetGitSubmodulesAsync(currentFlow.TargetSha),
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

        // TODO: Detect if no changes
        // TODO: Technically, if we only changed metadata files, there are no updates still
        return await _vmrUpdater.UpdateRepository(
            mapping,
            build,
            resetToRemoteWhenCloningRepo: ShouldResetClones,
            cancellationToken);
    }

    protected override async Task<bool> TryResolvingConflict(
        string mappingName,
        ILocalGitRepo repo,
        Build build,
        string filePath,
        CancellationToken cancellationToken)
    {
        // Known conflict in source-manifest.json
        if (string.Equals(filePath, VmrInfo.DefaultRelativeSourceManifestPath, StringComparison.OrdinalIgnoreCase))
        {
            await TryResolvingSourceManifestConflict(repo, mappingName!, cancellationToken);
            return true;
        }

        _logger.LogInformation("Auto-resolving conflict in {file} using PR version", filePath);
        await repo.RunGitCommandAsync(["checkout", "--ours", filePath], cancellationToken);
        await repo.StageAsync([filePath], cancellationToken);
        return true;
    }

    protected override IEnumerable<UnixPath> GetAllowedConflicts(IEnumerable<UnixPath> conflictedFiles, string mappingName) =>
    [
        // source-manifest.json
        VmrInfo.DefaultRelativeSourceManifestPath,

        // git-info for the repo
        new UnixPath($"{VmrInfo.GitInfoSourcesDir}/{mappingName}.props"),

        // Version files inside of the repo
        ..DependencyFileManager.DependencyFiles
            .Select(versionFile => VmrInfo.GetRelativeRepoSourcesPath(mappingName) / versionFile),

        // Common script files in the repo
        ..conflictedFiles
            .Where(f => f.Path.ToLowerInvariant().StartsWith(Constants.CommonScriptFilesPath + '/'))
    ];

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

    protected override NativePath GetEngCommonPath(NativePath sourceRepo) => sourceRepo / Constants.CommonScriptFilesPath;
    protected override bool TargetRepoIsVmr() => true;
    // When flowing local repos, we should never reset branches to the remote ones, we might lose some changes devs wanted
    protected virtual bool ShouldResetVmr => false;
    // In forward flow, we're flowing a specific commit, so we should just check it out, no need to sync local branch to remote
    protected virtual bool ShouldResetClones => false;
}
