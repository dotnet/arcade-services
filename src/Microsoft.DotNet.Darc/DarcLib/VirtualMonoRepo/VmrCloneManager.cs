// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
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
        ILogger<VmrPatchHandler> logger)
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
        var repo = await PrepareCloneInternalAsync(
            Constants.VmrFolderName,
            remoteUris,
            requestedRefs,
            checkoutRef,
            cancellationToken);

        _vmrInfo.VmrPath = repo.Path;
        _vmrInfo.VmrUri = remoteUris.First();

        await _dependencyTracker.InitializeSourceMappings();
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);

        return repo;
    }

    public async Task<ILocalGitRepo> PrepareVmrAsync(string checkoutRef, CancellationToken cancellationToken)
        => await PrepareVmrAsync(
            [_vmrInfo.VmrUri],
            [checkoutRef],
            checkoutRef,
            cancellationToken);

    protected override NativePath GetClonePath(string dirName) => _vmrInfo.VmrPath;
}
