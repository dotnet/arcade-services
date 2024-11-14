// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrCloneManager
{
    /// <summary>
    /// Prepares the local VMR clone by fetching from given remotes one-by-one until all requested commits are available.
    /// </summary>
    /// <param name="remoteUris">Remotes to fetch from one by one</param>
    /// <param name="requestedRefs">List of refs that need to be available</param>
    /// <param name="checkoutRef">Ref to check out at the end</param>
    /// <returns>Path to the clone</returns>
    Task<ILocalGitRepo> PrepareVmrAsync(
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        CancellationToken cancellationToken);

    Task<ILocalGitRepo> PrepareVmrAsync(
        string checkoutRef,
        CancellationToken cancellationToken);
}

public class VmrCloneManager : CloneManager, IVmrCloneManager
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ISourceManifest _sourceManifest;

    public VmrCloneManager(
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        ISourceManifest sourceManifest,
        IGitRepoCloner gitRepoCloner,
        ILocalGitClient localGitRepo,
        ILocalGitRepoFactory localGitRepoFactory,
        ITelemetryRecorder telemetryRecorder,
        IFileSystem fileSystem,
        ILogger<VmrCloneManager> logger)
        : base(vmrInfo, gitRepoCloner, localGitRepo, localGitRepoFactory, telemetryRecorder, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _sourceManifest = sourceManifest;
    }

    public async Task<ILocalGitRepo> PrepareVmrAsync(
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        CancellationToken cancellationToken)
    {
        ILocalGitRepo vmr = await PrepareCloneInternalAsync(
            Constants.VmrFolderName,
            remoteUris,
            requestedRefs,
            checkoutRef,
            cancellationToken);

        await _dependencyTracker.InitializeSourceMappings();
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);

        return vmr;
    }

    public async Task<ILocalGitRepo> PrepareVmrAsync(string checkoutRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"DJURADJ cloning VMR from {_vmrInfo.VmrUri}");
        ILocalGitRepo vmr = await PrepareVmrAsync(
            [_vmrInfo.VmrUri],
            [checkoutRef],
            checkoutRef,
            cancellationToken);

        _vmrInfo.VmrPath = vmr.Path;
        return vmr;
    }

    protected override NativePath GetClonePath(string dirName) => _vmrInfo.VmrPath;
}
