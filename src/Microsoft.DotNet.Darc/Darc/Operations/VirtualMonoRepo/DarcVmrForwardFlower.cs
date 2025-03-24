// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
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

public interface IDarcVmrForwardFlower
{
    /// <summary>
    /// Flows forward the code from a local clone of a repo to a local clone of the VMR.
    /// </summary>
    Task FlowForwardAsync(
        NativePath repoPath,
        string mappingName,
        string refToFlow,
        CodeFlowParameters flowOptions,
        CancellationToken cancellationToken);
}

internal class DarcVmrForwardFlower : VmrForwardFlower, IDarcVmrForwardFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly ILogger<VmrCodeFlower> _logger;

    public DarcVmrForwardFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            ICodeFlowVmrUpdater vmrUpdater,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler patchHandler,
            IProcessManager processManager,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, vmrUpdater, dependencyTracker, vmrCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, processManager, fileSystem, barClient, logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _localGitRepoFactory = localGitRepoFactory;
        _patchHandler = patchHandler;
        _logger = logger;
    }

    public async Task FlowForwardAsync(
        NativePath repoPath,
        string mappingName,
        string refToFlow,
        CodeFlowParameters flowOptions,
        CancellationToken cancellationToken)
    {
        var sourceRepo = _localGitRepoFactory.Create(repoPath);
        var shaToFlow = await sourceRepo.GetShaForRefAsync(refToFlow);

        _logger.LogInformation(
            "Flowing {repo}'s commit {repoSha} to the VMR at {targetDirectory}...",
            mappingName,
            DarcLib.Commit.GetShortSha(shaToFlow),
            _vmrInfo.VmrPath);

        await _dependencyTracker.RefreshMetadata();

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        Codeflow lastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: false);
        ForwardFlow currentFlow = new(lastFlow.TargetSha, refToFlow);

        var currentRepoBranch = await sourceRepo.GetCheckedOutBranchAsync();
        var currentVmrBranch = await vmr.GetCheckedOutBranchAsync();

        // We create a temporary branch at the current checkout
        // We flow the changes into another temporary branch
        // Later we merge tmpBranch2 into tmpBranch1
        // Then we look at the diff and stage that from the original VMR checkout
        // This way user only sees the staged files
        var tmpTargetBranch = "darc/tmp/" + Guid.NewGuid().ToString();
        var tmpHeadBranch = "darc/tmp/" + Guid.NewGuid().ToString();

        try
        {
            await vmr.CreateBranchAsync(tmpTargetBranch, true);
            await vmr.CreateBranchAsync(tmpHeadBranch, true);

            var build = new Build(-1, DateTimeOffset.Now, 0, false, false, shaToFlow, [], [], [], [])
            {
                GitHubRepository = repoPath
            };

            bool hasChanges = await FlowCodeAsync(
                lastFlow,
                currentFlow,
                sourceRepo,
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
                _logger.LogInformation("No changes to flow from {repo} to the VMR.", mapping.Name);
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
                // TODO: Create a new branch from wherever the base of the head branch is
                // TODO: Then stage the changes from the head branch and warn user that the branch is behind
                _logger.LogWarning("Failed to flow changes on top of the checked out VMR commit. " +
                    "Possibly, your repository is behind. " +
                    "Changes are prepared in the {branch} branch.",
                    tmpHeadBranch);
                return;
            }

            await StageChangesFromBranch(mappingName, vmr, currentVmrBranch, tmpHeadBranch, cancellationToken);
        }
        catch
        {
            await vmr.ResetWorkingTree();
            await vmr.CheckoutAsync(currentVmrBranch);
            throw;
        }
        finally
        {
            await sourceRepo.CheckoutAsync(currentRepoBranch);

            // Clean up the temporary branches
            try
            {
                await vmr.DeleteBranchAsync(tmpTargetBranch);
            }
            catch
            {
            }
            try
            {
                await vmr.DeleteBranchAsync(tmpHeadBranch);
            }
            catch
            {
            }
        }

    }

    private async Task StageChangesFromBranch(
        string mappingName,
        ILocalGitRepo vmr,
        string checkedOutBranch,
        string branchWithChanges,
        CancellationToken cancellationToken)
    {
        await vmr.CheckoutAsync(checkedOutBranch);

        var patchName = _vmrInfo.TmpPath / (Guid.NewGuid() + ".patch");
        var patches = await _patchHandler.CreatePatches(
            patchName,
            await vmr.GetShaForRefAsync(checkedOutBranch),
            await vmr.GetShaForRefAsync(branchWithChanges),
            path: null,
            [.. GetIgnoredFiles(mappingName).Select(VmrPatchHandler.GetExclusionRule)],
            relativePaths: false,
            workingDir: vmr.Path,
            applicationPath: null,
            cancellationToken);

        foreach (var patch in patches)
        {
            await _patchHandler.ApplyPatch(patch, vmr.Path, removePatchAfter: true, cancellationToken: cancellationToken);
        }

        _logger.LogInformation("Changes staged in {vmrPath}", vmr.Path);
    }

    protected override bool ShouldResetVmr => false;

    /// <summary>
    /// Returns a list of files that should be ignored when flowing changes forward.
    /// These mostly include code flow metadata which should not get updated in local flows.
    /// </summary>
    private static IEnumerable<string> GetIgnoredFiles(string mapping) =>
    [
        VmrInfo.DefaultRelativeSourceManifestPath,
        VmrInfo.GitInfoSourcesDir,
        VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.VersionDetailsXml,
    ];
}
