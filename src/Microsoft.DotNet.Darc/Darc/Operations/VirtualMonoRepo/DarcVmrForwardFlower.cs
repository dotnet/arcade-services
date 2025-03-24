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
    private readonly ISourceManifest _sourceManifest;
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
        _sourceManifest = sourceManifest;
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
        ISourceComponent repoVersion = _sourceManifest.GetRepoVersion(mapping.Name);

        var remotes = new[] { mapping.DefaultRemote, repoVersion.RemoteUri, repoPath }
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToList();

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

            // TODO: Do something better about this?
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

            await TryMergingBranch(
                mapping.Name,
                vmr,
                build,
                [],
                tmpTargetBranch,
                tmpHeadBranch,
                cancellationToken);

            await vmr.CheckoutAsync(currentVmrBranch);

            var patchName = _vmrInfo.TmpPath / (Guid.NewGuid() + ".patch");
            var patches = await _patchHandler.CreatePatches(
                patchName,
                await vmr.GetShaForRefAsync(currentVmrBranch),
                await vmr.GetShaForRefAsync(tmpHeadBranch),
                path: null,
                [
                    // Do not include version files as they would contain the fake build metadata
                    VmrPatchHandler.GetExclusionRule(VmrInfo.DefaultRelativeSourceManifestPath),
                    VmrPatchHandler.GetExclusionRule(VmrInfo.GitInfoSourcesDir),
                    VmrPatchHandler.GetExclusionRule(VmrInfo.GetRelativeRepoSourcesPath(mappingName) / VersionFiles.VersionDetailsXml),
                ],
                relativePaths: false,
                workingDir: _vmrInfo.VmrPath,
                applicationPath: null,
                cancellationToken);

            foreach (var patch in patches)
            {
                await _patchHandler.ApplyPatch(patch, _vmrInfo.VmrPath, removePatchAfter: true, cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Changes staged in {vmrPath}", _vmrInfo.VmrPath);
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

    protected override bool ShouldResetVmr => false;
}
