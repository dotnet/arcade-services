// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Helpers;

/// <summary>
/// Interface for VmrBackFlower used in the context of the PCS.
/// </summary>
public interface IDarcVmrBackFlower
{
    /// <summary>
    /// Flows code back from a local clone of a VMR into a local clone of a given repository.
    /// </summary>
    Task FlowBackAsync(
        NativePath repoPath,
        string mappingName,
        CodeFlowParameters flowOptions);
}

internal class DarcVmrBackFlower : VmrBackFlower, IDarcVmrBackFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly ILogger<VmrCodeFlower> _logger;

    public DarcVmrBackFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            IRepositoryCloneManager repositoryCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler vmrPatchHandler,
            IWorkBranchFactory workBranchFactory,
            IVersionFileCodeFlowUpdater versionFileConflictResolver,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, vmrCloneManager, repositoryCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, vmrPatchHandler, workBranchFactory, versionFileConflictResolver, fileSystem, barClient, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _localGitRepoFactory = localGitRepoFactory;
        _logger = logger;
    }

    public async Task FlowBackAsync(
        NativePath repoPath,
        string mappingName,
        CodeFlowParameters flowOptions)
    {
        var sourceRepo = _localGitRepoFactory.Create(repoPath);
        var sourceSha = await sourceRepo.GetShaForRefAsync();

        _logger.LogInformation(
            "Flowing current repo commit {repoSha} to VMR {targetDirectory}...",
            Commit.GetShortSha(sourceSha),
            _vmrInfo.VmrPath);

        await _dependencyTracker.RefreshMetadata();
        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);

        // TODO https://github.com/dotnet/arcade-services/issues/4515: Call base.FlowBackAsync()
        throw new NotImplementedException("Command not supported yet");
    }

    protected override bool ShouldResetVmr => false;
}
