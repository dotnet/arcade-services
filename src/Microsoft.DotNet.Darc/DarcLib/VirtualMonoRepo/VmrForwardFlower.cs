// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Maestro.Client.Models;
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
    /// <param name="shaToFlow">SHA to flow</param>
    /// <param name="buildToFlow">Build to flow</param>
    /// <param name="baseBranch">If target branch does not exist, it is created off of this branch</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <param name="discardPatches">Keep patch files?</param>
    /// <returns>True when there were changes to be flown</returns>
    Task<bool> FlowForwardAsync(
        string mapping,
        NativePath sourceRepo,
        string? shaToFlow,
        int? buildToFlow,
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
        _logger = logger;
    }

    public async Task<bool> FlowForwardAsync(
        string mappingName,
        NativePath repoPath,
        string? shaToFlow,
        int? buildToFlow,
        string baseBranch,
        string targetBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
    {
        bool targetBranchExisted = await PrepareVmr(baseBranch, targetBranch, cancellationToken);

        Build? build = null;
        if (buildToFlow.HasValue)
        {
            build = await _barClient.GetBuildAsync(buildToFlow.Value)
                ?? throw new Exception($"Failed to find build with BAR ID {buildToFlow}");
        }

        ILocalGitRepo sourceRepo = _localGitRepoFactory.Create(repoPath);
        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ISourceComponent repoInfo = _sourceManifest.GetRepoVersion(mappingName);

        // Refresh the repo
        await sourceRepo.FetchAllAsync([mapping.DefaultRemote, repoInfo.RemoteUri], cancellationToken);

        // SHA comes either directly or from the build or if none supplied, from tip of the repo
        shaToFlow ??= build?.Commit;
        if (shaToFlow is null)
        {
            shaToFlow = await sourceRepo.GetShaForRefAsync();
        }
        else
        {
            await sourceRepo.CheckoutAsync(shaToFlow);
        }

        Codeflow lastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: false);

        bool hasChanges = await FlowCodeAsync(
            lastFlow,
            new ForwardFlow(lastFlow.TargetSha, shaToFlow),
            sourceRepo,
            mapping,
            build,
            baseBranch,
            targetBranch,
            discardPatches,
            rebaseConflicts: !targetBranchExisted,
            cancellationToken);

        hasChanges |= await UpdateDependenciesAndToolset(
            sourceRepo.Path,
            LocalVmr,
            build,
            sourceElementSha: null,
            cancellationToken);

        return hasChanges;
    }

    protected async Task<bool> PrepareVmr(string baseBranch, string targetBranch, CancellationToken cancellationToken)
    {
        bool branchExisted;

        try
        {
            await _vmrCloneManager.PrepareVmrAsync(
                [_vmrInfo.VmrUri],
                [baseBranch, targetBranch],
                targetBranch,
                cancellationToken);
            branchExisted = true;
        }
        catch (NotFoundException)
        {
            // This means the target branch does not exist yet
            // We will create it off of the base branch
            await LocalVmr.CheckoutAsync(baseBranch);
            await LocalVmr.CreateBranchAsync(targetBranch);
            branchExisted = false;
        }

        await _dependencyTracker.InitializeSourceMappings();
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);
        return branchExisted;
    }

    protected override async Task<bool> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        Build? build,
        string baseBranch,
        string targetBranch,
        bool discardPatches,
        bool rebaseConflicts,
        CancellationToken cancellationToken)
    {
        string branchName = currentFlow.GetBranchName();

        List<AdditionalRemote> additionalRemotes =
        [
            new AdditionalRemote(mapping.Name, sourceRepo.Path)
        ];

        if (build is not null)
        {
            additionalRemotes.Add(new AdditionalRemote(mapping.Name, build.GetRepository()));
        }

        bool hadUpdates;

        try
        {
            // If the build produced any assets, we use the number to update VMR's git info files
            // The git info files won't be important by then and probably removed but let's keep it for now
            string? targetVersion = null;
            if (build?.Assets.Count > 0)
            {
                targetVersion = build?.Assets[0].Version;
            }

            hadUpdates = await _vmrUpdater.UpdateRepository(
                mapping.Name,
                currentFlow.TargetSha,
                targetVersion,
                updateDependencies: false,
                additionalRemotes: additionalRemotes,
                componentTemplatePath: null,
                tpnTemplatePath: null,
                generateCodeowners: true,
                generateCredScanSuppressions: true,
                discardPatches,
                cancellationToken);
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
            await _vmrCloneManager.PrepareVmrAsync(previousFlowTargetSha, cancellationToken);
            await LocalVmr.CreateBranchAsync(targetBranch, overwriteExistingBranch: true);

            // Reconstruct the previous flow's branch
            var lastLastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: true);
            await FlowCodeAsync(
                lastLastFlow,
                new ForwardFlow(lastLastFlow.SourceSha, lastFlow.SourceSha),
                sourceRepo,
                mapping,
                build,
                targetBranch, // TODO: This is an interesting one - should we try to find a build for that previous SHA?
                targetBranch,
                discardPatches,
                rebaseConflicts,
                cancellationToken);

            // We apply the current changes on top again - they should apply now
            // TODO https://github.com/dotnet/arcade-services/issues/2995: Handle exceptions
            hadUpdates = await _vmrUpdater.UpdateRepository(
                mapping.Name,
                currentFlow.TargetSha,
                // TODO - all parameters below should come from BAR build / options
                "1.2.3",
                updateDependencies: false,
                additionalRemotes,
                componentTemplatePath: null,
                tpnTemplatePath: null,
                generateCodeowners: false,
                generateCredScanSuppressions: false,
                discardPatches,
                cancellationToken);
        }

        return hadUpdates;
    }

    protected override async Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        Build? build,
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
        // When flowing to the VMR, we remove all files but sobmodules and cloaked files
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
            sourceRepo.Path, // TODO = URL from BAR build
            Constants.EmptyGitObject,
            _dependencyTracker.GetDependencyVersion(mapping)!.PackageVersion,
            Parent: null));

        IReadOnlyCollection<AdditionalRemote>? additionalRemote = build is not null
            ? [new AdditionalRemote(mapping.Name, build.GetRepository())]
            : [];

        string? targetVersion = null;
        if (build?.Assets.Count > 0)
        {
            targetVersion = build.Assets[0].Version;
        }

        // TODO: Detect if no changes
        // TODO: Technically, if we only changed metadata files, there are no updates still
        return await _vmrUpdater.UpdateRepository(
            mapping.Name,
            currentFlow.TargetSha,
            targetVersion,
            updateDependencies: false,
            additionalRemote,
            componentTemplatePath: null,
            tpnTemplatePath: null,
            generateCodeowners: false,
            generateCredScanSuppressions: false,
            discardPatches,
            cancellationToken);
    }
}
