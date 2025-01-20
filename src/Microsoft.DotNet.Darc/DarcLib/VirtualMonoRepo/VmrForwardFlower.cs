// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.DarcLib.Conflicts;
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
    /// <param name="baseBranch">If target branch does not exist, it is created off of this branch</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <param name="discardPatches">Keep patch files?</param>
    /// <returns>True when there were changes to be flown</returns>
    Task<bool> FlowForwardAsync(
        string mapping,
        NativePath sourceRepo,
        int buildToFlow,
        IReadOnlyCollection<string>? excludedAssets,
        string baseBranch,
        string targetBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flows forward the code from the source repo to the target branch of the VMR.
    /// This overload is used in the context of the darc CLI.
    /// </summary>
    /// <param name="mapping">Mapping to flow</param>
    /// <param name="sourceRepo">Local checkout of the repository</param>
    /// <param name="buildToFlow">Build to flow</param>
    /// <param name="excludedAssets">Assets to exclude from the dependency flow</param>
    /// <param name="baseBranch">If target branch does not exist, it is created off of this branch</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <param name="discardPatches">Keep patch files?</param>
    /// <returns>True when there were changes to be flown</returns>
    Task<bool> FlowForwardAsync(
        string mapping,
        NativePath sourceRepo,
        Build buildToFlow,
        IReadOnlyCollection<string>? excludedAssets,
        string baseBranch,
        string targetBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);
}

internal class VmrForwardFlower : VmrCodeFlower, IVmrForwardFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrUpdater _vmrUpdater;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly IBasicBarClient _barClient;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IProcessManager _processManager;
    private readonly IForwardFlowConflictResolver _conflictResolver;
    private readonly ILogger<VmrCodeFlower> _logger;

    public VmrForwardFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrUpdater vmrUpdater,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            IDependencyFileManager dependencyFileManager,
            ILocalGitClient localGitClient,
            ILocalLibGit2Client libGit2Client,
            IBasicBarClient basicBarClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IProcessManager processManager,
            ICoherencyUpdateResolver coherencyUpdateResolver,
            IAssetLocationResolver assetLocationResolver,
            IForwardFlowConflictResolver conflictResolver,
            IFileSystem fileSystem,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, libGit2Client, localGitRepoFactory, versionDetailsParser, dependencyFileManager, coherencyUpdateResolver, assetLocationResolver, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _vmrUpdater = vmrUpdater;
        _dependencyTracker = dependencyTracker;
        _vmrCloneManager = vmrCloneManager;
        _barClient = basicBarClient;
        _localGitRepoFactory = localGitRepoFactory;
        _processManager = processManager;
        _conflictResolver = conflictResolver;
        _logger = logger;
    }

    public async Task<bool> FlowForwardAsync(
        string mappingName,
        NativePath repoPath,
        int buildToFlow,
        IReadOnlyCollection<string>? excludedAssets,
        string baseBranch,
        string targetBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
        => await FlowForwardAsync(
            mappingName,
            repoPath,
            await _barClient.GetBuildAsync(buildToFlow)
                ?? throw new Exception($"Failed to find build with BAR ID {buildToFlow}"),
            excludedAssets,
            baseBranch,
            targetBranch,
            discardPatches,
            cancellationToken);

    public async Task<bool> FlowForwardAsync(
        string mappingName,
        NativePath repoPath,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string baseBranch,
        string targetBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
    {
        bool targetBranchExisted = await PrepareVmr(_vmrInfo.VmrUri, baseBranch, targetBranch, cancellationToken);

        ILocalGitRepo sourceRepo = _localGitRepoFactory.Create(repoPath);
        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);

        return await FlowForwardAsync(
            mapping,
            sourceRepo,
            build,
            excludedAssets,
            baseBranch,
            targetBranch,
            targetBranchExisted,
            discardPatches,
            cancellationToken);
    }

    protected async Task<bool> FlowForwardAsync(
        SourceMapping mapping,
        ILocalGitRepo sourceRepo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string baseBranch,
        string targetBranch,
        bool targetBranchExisted,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
    {
        ISourceComponent repoInfo = _sourceManifest.GetRepoVersion(mapping.Name);

        // Refresh the repo
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
            baseBranch,
            targetBranch,
            discardPatches,
            rebaseConflicts: !targetBranchExisted,
            cancellationToken);

        if (hasChanges)
        {
            // We try to merge the target branch so that we can potentially
            // resolve some expected conflicts in the version files
            ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
            await _conflictResolver.TryMergingBranch(vmr, mapping.Name, targetBranch, baseBranch);
        }

        return hasChanges;
    }

    protected async Task<bool> PrepareVmr(
        string vmrUri,
        string baseBranch,
        string targetBranch,
        CancellationToken cancellationToken)
    {
        bool branchExisted;

        try
        {
            await _vmrCloneManager.PrepareVmrAsync(
                vmrUri,
                [baseBranch, targetBranch],
                targetBranch,
                ShouldResetBranchToRemoteWhenPreparingVmr(),
                cancellationToken);
            branchExisted = true;
        }
        catch (NotFoundException)
        {
            // This means the target branch does not exist yet
            // We will create it off of the base branch
            var vmr = await _vmrCloneManager.PrepareVmrAsync(
                vmrUri,
                [baseBranch],
                baseBranch,
                ShouldResetBranchToRemoteWhenPreparingVmr(),
                cancellationToken);

            await vmr.CheckoutAsync(baseBranch);
            await vmr.CreateBranchAsync(targetBranch);
            branchExisted = false;
        }

        return branchExisted;
    }

    protected override async Task<bool> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string baseBranch,
        string targetBranch,
        bool discardPatches,
        bool rebaseConflicts,
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
                mapping.Name,
                currentFlow.TargetSha,
                targetVersion,
                build.AzureDevOpsBuildNumber,
                build.Id,
                updateDependencies: false,
                additionalRemotes: additionalRemotes,
                tpnTemplatePath: _vmrInfo.VmrPath / VmrInfo.ThirdPartyNoticesTemplatePath,
                generateCodeowners: false,
                generateCredScanSuppressions: true,
                discardPatches,
                reapplyVmrPatches: true,
                lookUpBuilds: true,
                amendReapplyCommit: true,
                resetToRemoteWhenCloningRepo: ShouldResetBranchToRemoteWhenPreparingRepo(),
                cancellationToken: cancellationToken);
        }
        catch (PatchApplicationFailedException e)
        {
            // When we are updating an already existing PR branch, there can be conflicting changes in the PR from devs.
            if (!rebaseConflicts)
            {
                _logger.LogInformation("Failed to update a PR branch because of a conflict. Stopping the flow..");
                throw new ConflictInPrBranchException(e.Patch, targetBranch);
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
                _vmrInfo.VmrUri,
                [previousFlowTargetSha],
                previousFlowTargetSha,
                ShouldResetBranchToRemoteWhenPreparingVmr(),
                cancellationToken);
            await vmr.CreateBranchAsync(targetBranch, overwriteExistingBranch: true);

            // Reconstruct the previous flow's branch
            var lastLastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: true);
            await FlowCodeAsync(
                lastLastFlow,
                new ForwardFlow(lastLastFlow.SourceSha, lastFlow.SourceSha),
                sourceRepo,
                mapping,
                // TODO (https://github.com/dotnet/arcade-services/issues/4166): Find a previous build?
                new Build(-1, DateTimeOffset.Now, 0, false, false, lastLastFlow.SourceSha, [], [], [], []),
                excludedAssets,
                baseBranch,
                targetBranch,
                discardPatches,
                rebaseConflicts,
                cancellationToken);

            // We apply the current changes on top again - they should apply now
            // TODO https://github.com/dotnet/arcade-services/issues/2995: Handle exceptions
            hadUpdates = await _vmrUpdater.UpdateRepository(
                mapping.Name,
                currentFlow.TargetSha,
                build.Assets.FirstOrDefault()?.Version ?? "0.0.0",
                build.AzureDevOpsBuildNumber,
                build.Id,
                updateDependencies: false,
                additionalRemotes,
                tpnTemplatePath: _vmrInfo.VmrPath / VmrInfo.ThirdPartyNoticesTemplatePath,
                generateCodeowners: false,
                generateCredScanSuppressions: false,
                discardPatches,
                reapplyVmrPatches: true,
                lookUpBuilds: true,
                amendReapplyCommit: true,
                resetToRemoteWhenCloningRepo: ShouldResetBranchToRemoteWhenPreparingRepo(),
                cancellationToken: cancellationToken);
        }

        return hadUpdates;
    }

    protected override async Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        Build build,
        string baseBranch,
        string targetBranch,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        await sourceRepo.CheckoutAsync(lastFlow.TargetSha);

        var patchName = _vmrInfo.TmpPath / $"{targetBranch.Replace('/', '-')}.patch";
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

        IReadOnlyCollection<AdditionalRemote>? additionalRemote = [new AdditionalRemote(mapping.Name, build.GetRepository())];

        var targetVersion = build.Assets.FirstOrDefault()?.Version;

        // TODO: Detect if no changes
        // TODO: Technically, if we only changed metadata files, there are no updates still
        return await _vmrUpdater.UpdateRepository(
            mapping.Name,
            currentFlow.TargetSha,
            targetVersion,
            build.AzureDevOpsBuildNumber,
            build.Id,
            updateDependencies: false,
            additionalRemote,
            tpnTemplatePath: _vmrInfo.VmrPath / VmrInfo.ThirdPartyNoticesTemplatePath,
            generateCodeowners: false,
            generateCredScanSuppressions: true,
            discardPatches,
            reapplyVmrPatches: true,
            lookUpBuilds: true,
            amendReapplyCommit: true,
            resetToRemoteWhenCloningRepo: ShouldResetBranchToRemoteWhenPreparingRepo(),
            cancellationToken: cancellationToken);
    }

    protected override NativePath GetEngCommonPath(NativePath sourceRepo) => sourceRepo / Constants.CommonScriptFilesPath;
    protected override bool TargetRepoIsVmr() => true;
    // When flowing local repos, we should never reset branches to the remote ones, we might lose some changes devs wanted
    protected override bool ShouldResetBranchToRemoteWhenPreparingVmr() => false;
    // In forward flow, we're flowing a specific commit, so we should just check it out, no need to sync local branch to remote
    protected override bool ShouldResetBranchToRemoteWhenPreparingRepo() => false;
}
