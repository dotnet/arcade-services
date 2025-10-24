﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
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

    /// <summary>
    /// Registers a known local location that contains a VMR clone.
    /// </summary>
    Task RegisterVmrAsync(NativePath localPath);
}

public class VmrCloneManager : CloneManager, IVmrCloneManager
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitClient _localGitRepo;

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
        _localGitRepo = localGitRepo;
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
        var folderName = StringUtils.GetXxHash64(
            string.Join(';', remoteUris.Distinct().OrderBy(u => u)));

        return await PrepareVmrAsync(
            _vmrInfo.TmpPath / Path.Combine("vmrs", folderName),
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
        await _dependencyTracker.RefreshMetadataAsync();

        return vmr;
    }

    public async Task RegisterVmrAsync(NativePath localPath)
    {
        var remotes = await _localGitRepo.GetRemotesAsync(localPath);
        var branch = await _localGitRepo.GetCheckedOutBranchAsync(localPath);

        if (remotes.Count == 0 || string.IsNullOrEmpty(branch))
        {
            throw new DarcException($"The provided path '{localPath}' does not appear to be a git repository.");
        }

        await PrepareCloneInternalAsync(localPath, [remotes[0].Uri], [branch], branch, false);
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
