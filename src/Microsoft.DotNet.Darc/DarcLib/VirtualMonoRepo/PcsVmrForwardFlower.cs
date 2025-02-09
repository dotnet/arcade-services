// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Interface for VmrForwardFlower used in the context of the PCS.
/// </summary>
public interface IPcsVmrForwardFlower
{
    /// <summary>
    /// Flows forward the code from the source repo to the target branch of the VMR.
    /// This overload is used in the context of the PCS.
    /// </summary>
    /// <param name="subscription">Subscription to flow</param>
    /// <param name="build">Build to flow</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <returns>True when there were changes to be flown</returns>
    Task<CodeFlowResult> FlowForwardAsync(
        Subscription subscription,
        Build build,
        string headBranch,
        CancellationToken cancellationToken = default);
}

internal class PcsVmrForwardFlower : VmrForwardFlower, IPcsVmrForwardFlower
{
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IRepositoryCloneManager _repositoryCloneManager;

    public PcsVmrForwardFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        ICodeFlowVmrUpdater vmrUpdater,
        IVmrDependencyTracker dependencyTracker,
        IVmrCloneManager vmrCloneManager,
        IRepositoryCloneManager repositoryCloneManager,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, vmrUpdater, dependencyTracker, vmrCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, processManager, fileSystem, logger)
    {
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _repositoryCloneManager = repositoryCloneManager;
    }

    public async Task<CodeFlowResult> FlowForwardAsync(
        Subscription subscription,
        Build build,
        string headBranch,
        CancellationToken cancellationToken = default)
    {
        bool headBranchExisted = await PrepareVmr(subscription.TargetRepository, subscription.TargetBranch, headBranch, cancellationToken);
        SourceMapping mapping = _dependencyTracker.GetMapping(subscription.TargetDirectory);
        ISourceComponent repoVersion = _sourceManifest.GetRepoVersion(mapping.Name);
        List<string> remotes = new[] { mapping.DefaultRemote, repoVersion.RemoteUri }
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToList();

        ILocalGitRepo sourceRepo = await _repositoryCloneManager.PrepareCloneAsync(
            mapping,
            remotes,
            build.Commit,
            ShouldResetClones,
            cancellationToken);

        return await FlowForwardAsync(
            mapping.Name,
            sourceRepo.Path,
            build,
            subscription.ExcludedAssets,
            subscription.TargetBranch,
            headBranch,
            subscription.TargetRepository,
            discardPatches: true,
            headBranchExisted,
            cancellationToken);
    }

    // During forward flow, we're targeting a specific remote VMR branch, so we should make sure our local branch is reset to it
    protected override bool ShouldResetVmr => true;
}
