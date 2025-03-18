// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
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
        IReadOnlyCollection<AdditionalRemote> additionalRemotes);
}

internal class DarcVmrForwardFlower : VmrForwardFlower, IDarcVmrForwardFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
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
        _logger = logger;
    }

    public async Task FlowForwardAsync(
        NativePath repoPath,
        string mappingName,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes)
    {
        var sourceRepo = _localGitRepoFactory.Create(repoPath);
        var sourceSha = await sourceRepo.GetShaForRefAsync();

        _logger.LogInformation(
            "Flowing current repo commit {repoSha} to VMR {targetDirectory}...",
            Commit.GetShortSha(sourceSha),
            _vmrInfo.VmrPath);

        await _dependencyTracker.RefreshMetadata();
        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ISourceComponent repoVersion = _sourceManifest.GetRepoVersion(mapping.Name);

        var remotes = new[] { mapping.DefaultRemote, repoVersion.RemoteUri }
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToList();

        // TODO: Call FlowForward
    }

    protected override bool ShouldResetVmr => false;
}
