// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrCloneManager : ICloneManager
{
    /// <summary>
    /// Prepares the local VMR clone by fetching from given remotes one-by-one until all requested commits are available.
    /// </summary>
    /// <param name="remoteUris">Remotes to fetch from one by one</param>
    /// <param name="requestedRefs">List of refs that need to be available</param>
    /// <param name="checkoutRef">Ref to check out at the end</param>
    /// <param name="resetToRemote">Whether to reset to the remote ref after fetching</param>
    /// <returns>Path to the clone</returns>
    Task<ILocalGitRepo> PrepareVmrAsync(
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Path to an already cloned VMR we want to use.
    /// </summary>
    Task<ILocalGitRepo> PrepareVmrAsync(
        NativePath vmrPath,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default);
}

public class VmrCloneManager : CloneManager, IVmrCloneManager
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;

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
    }

    public async Task<ILocalGitRepo> PrepareVmrAsync(
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default)
    {
        // This makes sure we keep different VMRs separate
        // We expect to have up to 3:
        // 1. The GitHub VMR (dotnet/dotnet)
        // 2. The AzDO mirror (dotnet-dotnet)
        // 3. The E2E test VMR (maestro-auth-tests/maestro-test-vmr)

        NativePath vmrPath;
        if (_clones.TryGetValue(remoteUris.First(), out var cachedVmrPath))
        {
            vmrPath = cachedVmrPath;
        }
        else
        {
            var folderName = StringUtils.GetXxHash64(
                string.Join(';', remoteUris.Distinct().OrderBy(u => u)));

            vmrPath = _vmrInfo.TmpPath / "vmrs" / folderName;
        }

        return await PrepareVmrAsync(
            vmrPath,
            remoteUris,
            requestedRefs,
            checkoutRef,
            resetToRemote,
            cancellationToken);
    }

    public async Task<ILocalGitRepo> PrepareVmrAsync(
        NativePath vmrPath,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default)
    {
        ILocalGitRepo vmr = await PrepareCloneInternalAsync(
            vmrPath,
            remoteUris,
            requestedRefs,
            checkoutRef,
            resetToRemote,
            cancellationToken);

        _vmrInfo.VmrPath = vmr.Path;
        _vmrInfo.VmrUri = remoteUris.First();

        await _dependencyTracker.RefreshMetadataAsync();

        return vmr;
    }

    // When we initialize with a single static VMR,
    // we will have the path in the newly initialized VmrPath (from VmrRegistrations).
    // When we initialize with a different new VMR for each background job,
    // the vmrPath will be empty and we will set it to the suggested dirName.
    protected override NativePath GetClonePath(string dirName)
        => !string.IsNullOrEmpty(_vmrInfo.VmrPath)
            ? _vmrInfo.VmrPath
            : _vmrInfo.TmpPath / dirName;
}
