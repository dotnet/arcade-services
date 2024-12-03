// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.Maestro.Client.Models;
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
    Task<bool> FlowForwardAsync(
        Subscription subscription,
        Build build,
        string targetBranch,
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
        IVmrUpdater vmrUpdater,
        IVmrDependencyTracker dependencyTracker,
        IVmrCloneManager vmrCloneManager,
        IDependencyFileManager dependencyFileManager,
        IRepositoryCloneManager repositoryCloneManager,
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
        : base(vmrInfo, sourceManifest, vmrUpdater, dependencyTracker, vmrCloneManager, dependencyFileManager, localGitClient, libGit2Client, basicBarClient, localGitRepoFactory, versionDetailsParser, processManager, coherencyUpdateResolver, assetLocationResolver, fileSystem, logger)
    {
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _repositoryCloneManager = repositoryCloneManager;
    }

    public async Task<bool> FlowForwardAsync(
        Subscription subscription,
        Build build,
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        var baseBranch = subscription.TargetBranch;
        bool targetBranchExisted = await PrepareVmr(
            subscription.TargetRepository,
            baseBranch,
            targetBranch,
            cancellationToken);

        // Prepare repo
        SourceMapping mapping = _dependencyTracker.GetMapping(subscription.TargetDirectory);
        ISourceComponent repoVersion = _sourceManifest.GetRepoVersion(mapping.Name);
        List<string> remotes = (new[] { mapping.DefaultRemote, repoVersion.RemoteUri })
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToList();

        ILocalGitRepo sourceRepo = await _repositoryCloneManager.PrepareCloneAsync(
            mapping,
            remotes,
            build.Commit,
            cancellationToken);

        Codeflow lastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: false);

        return await FlowCodeAsync(
            lastFlow,
            new ForwardFlow(lastFlow.TargetSha, build.Commit),
            sourceRepo,
            mapping,
            build,
            subscription.ExcludedAssets,
            baseBranch,
            targetBranch,
            discardPatches: true,
            rebaseConflicts: !targetBranchExisted,
            cancellationToken);
    }
}
