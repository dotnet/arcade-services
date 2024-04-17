// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
/// Interface for VmrForwardFlower used in the context of the PCS.
/// </summary>
public interface IPcsVmrForwardFlower
{
    /// <summary>
    /// Flows forward the code from the source repo to the target branch of the VMR.
    /// This overload is used in the context of the PCS.
    /// </summary>
    /// <param name="mappingName">Mapping to flow</param>
    /// <param name="build">Build to flow</param>
    /// <param name="baseBranch">If target branch does not exist, it is created off of this branch</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <returns>True when there were changes to be flown</returns>
    Task<bool> FlowForwardAsync(
        string mappingName,
        Build build,
        string baseBranch,
        string targetBranch,
        CancellationToken cancellationToken = default);
}

internal class PcsVmrForwardFlower : VmrForwardFlower, IPcsVmrForwardFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrCloneManager _vmrCloneManager;
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
        IWorkBranchFactory workBranchFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IAssetLocationResolver assetLocationResolver,
        IFileSystem fileSystem,
        ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, vmrUpdater, dependencyTracker, vmrCloneManager, dependencyFileManager, repositoryCloneManager, localGitClient, libGit2Client, basicBarClient, localGitRepoFactory, versionDetailsParser, processManager, workBranchFactory, coherencyUpdateResolver, assetLocationResolver, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _vmrCloneManager = vmrCloneManager;
        _repositoryCloneManager = repositoryCloneManager;
    }

    public async Task<bool> FlowForwardAsync(
        string mappingName,
        Build build,
        string baseBranch,
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        await PrepareVmr(baseBranch, targetBranch, cancellationToken);

        // Prepare repo
        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        var remotes = new[] { mapping.DefaultRemote, _sourceManifest.GetRepoVersion(mapping.Name).RemoteUri }
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
            baseBranch,
            targetBranch,
            discardPatches: true,
            cancellationToken);
    }
}
