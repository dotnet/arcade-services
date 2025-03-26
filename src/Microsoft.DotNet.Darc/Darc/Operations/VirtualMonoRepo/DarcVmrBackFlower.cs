// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

/// <summary>
/// Interface for VmrBackFlower used in the context of the PCS.
/// </summary>
public interface IDarcVmrBackFlower
{
    /// <summary>
    /// Flows forward the code from a local clone of a repo to a local clone of the VMR.
    /// </summary>
    Task FlowBackAsync(
        NativePath repoPath,
        string mappingName,
        string refToFlow,
        CodeFlowParameters flowOptions,
        CancellationToken cancellationToken);
}

internal class DarcVmrBackFlower : VmrBackFlower, IDarcVmrBackFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IVersionFileCodeFlowUpdater _versionFileConflictResolver;
    private readonly ILogger<VmrCodeFlower> _logger;

    public DarcVmrBackFlower(
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
            IVersionFileCodeFlowUpdater versionFileConflictResolver,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, vmrCloneManager, repositoryCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, vmrPatchHandler, workBranchFactory, versionFileConflictResolver, fileSystem, barClient, logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _localGitRepoFactory = localGitRepoFactory;
        _patchHandler = vmrPatchHandler;
        _versionFileConflictResolver = versionFileConflictResolver;
        _logger = logger;
    }

    public async Task FlowBackAsync(
        NativePath repoPath,
        string mappingName,
        string refToFlow,
        CodeFlowParameters flowOptions,
        CancellationToken cancellationToken)
    {
        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        string shaToFlow = await vmr.GetShaForRefAsync(refToFlow);
        ILocalGitRepo targetRepo = _localGitRepoFactory.Create(repoPath);

        _logger.LogInformation(
            "Flowing VMR's commit {sourceSha} to {repo} at {targetDirectory}...",
            DarcLib.Commit.GetShortSha(shaToFlow),
            mappingName,
            repoPath);

        await _dependencyTracker.RefreshMetadata();

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);

        Codeflow lastFlow;
        try
        {
            lastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);
        }
        catch (InvalidSynchronizationException)
        {
            // We're trying to synchronize an old repo commit on top of a VMR commit that had other synchronization with the repo since.
            throw new InvalidSynchronizationException(
                "Failed to flow changes on top of the checked out repo. " +
                "Possibly, the VMR is out of sync with the repository - " +
                "one is behind and a more recent code flow happened since. " +
                "Please rebase your VMR branch and refresh the repository.");
        }

        Backflow currentFlow = new(refToFlow, lastFlow.RepoSha);

        string currentRepoBranch = await targetRepo.GetCheckedOutBranchAsync();
        string currentVmrBranch = await vmr.GetCheckedOutBranchAsync();

        // We create a temporary branch at the current checkout
        // We flow the changes into another temporary branch
        // Later we merge tmpBranch2 into tmpBranch1
        // Then we look at the diff and stage that from the original repo checkout
        // This way user only sees the staged files
        string tmpTargetBranch = "darc/tmp/" + Guid.NewGuid().ToString();
        string tmpHeadBranch = "darc/tmp/" + Guid.NewGuid().ToString();

        try
        {
            await targetRepo.CreateBranchAsync(tmpTargetBranch, true);
            await targetRepo.CreateBranchAsync(tmpHeadBranch, true);

            Build build = new(-1, DateTimeOffset.Now, 0, false, false, shaToFlow, [], [], [], [])
            {
                GitHubRepository = _vmrInfo.VmrPath,
            };

            bool hasChanges = await FlowCodeAsync(
                lastFlow,
                currentFlow,
                targetRepo,
                mapping,
                build,
                [],
                tmpTargetBranch,
                tmpHeadBranch,
                flowOptions.DiscardPatches,
                headBranchExisted: false,
                cancellationToken);

            if (!hasChanges)
            {
                _logger.LogInformation("No changes to flow from VMR to {repo}.", mapping.Name);
                await targetRepo.CheckoutAsync(currentRepoBranch);
                return;
            }

            try
            {
                await _versionFileConflictResolver.TryMergingBranchAndUpdateDependencies(
                    mapping,
                    lastFlow,
                    currentFlow,
                    targetRepo,
                    build,
                    tmpHeadBranch,
                    tmpTargetBranch,
                    excludedAssets: [],
                    headBranchExisted: false,
                    cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to backflow VMR into {repo}", mappingName);

                throw new InvalidSynchronizationException(
                    "Failed to flow changes on top of the checked out VMR commit - " +
                    "possibly due to conflicts. " +
                    $"Changes are ready in the {tmpHeadBranch} branch based on an older VMR commit.");
            }

            await StageChangesFromBranch(targetRepo, currentVmrBranch, tmpHeadBranch, cancellationToken);
        }
        catch
        {
            await targetRepo.ResetWorkingTree();
            await targetRepo.CheckoutAsync(currentRepoBranch);
            throw;
        }
        finally
        {
            _logger.LogInformation("Cleaning up...");

            try
            {
                await targetRepo.DeleteBranchAsync(tmpTargetBranch);
            }
            catch
            {
            }
            try
            {
                await targetRepo.DeleteBranchAsync(tmpHeadBranch);
            }
            catch
            {
            }

            await vmr.CheckoutAsync(currentVmrBranch);
        }

        _logger.LogInformation("Changes staged in {repoPath}", targetRepo.Path);
    }

    private async Task StageChangesFromBranch(
        ILocalGitRepo targetRepo,
        string checkedOutBranch,
        string branchWithChanges,
        CancellationToken cancellationToken)
    {
        await targetRepo.CheckoutAsync(checkedOutBranch);

        string patchName = _vmrInfo.TmpPath / (Guid.NewGuid() + ".patch");
        List<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            patchName,
            await targetRepo.GetShaForRefAsync(checkedOutBranch),
            await targetRepo.GetShaForRefAsync(branchWithChanges),
            path: null,
            filters: null,
            relativePaths: false,
            workingDir: targetRepo.Path,
            applicationPath: null,
            cancellationToken);

        foreach (VmrIngestionPatch patch in patches)
        {
            await _patchHandler.ApplyPatch(patch, targetRepo.Path, removePatchAfter: true, cancellationToken: cancellationToken);
        }
    }

    protected override bool ShouldResetVmr => false;
}
