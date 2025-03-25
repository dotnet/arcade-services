// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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

        Backflow currentFlow = new(lastFlow.VmrSha, refToFlow);

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
                return;
            }

            if (!await TryMergingBranch(
                mapping.Name,
                vmr,
                build,
                [],
                tmpTargetBranch,
                tmpHeadBranch,
                cancellationToken))
            {
                throw new InvalidSynchronizationException(
                    "Failed to flow changes on top of the checked out VMR commit - " +
                    "possibly due to conflicts. " +
                    $"Changes are ready in the {tmpHeadBranch} branch based on an older VMR commit.");
            }

            await StageChangesFromBranch(mappingName, vmr, currentVmrBranch, tmpHeadBranch, cancellationToken);
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

    protected override bool ShouldResetVmr => false;
}
