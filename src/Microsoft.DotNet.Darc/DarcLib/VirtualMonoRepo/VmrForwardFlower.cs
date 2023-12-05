// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrForwardFlower
{
    // TODO: Docs
    Task<string?> FlowForwardAsync(
        string mapping,
        NativePath sourceRepo,
        string? shaToFlow = null,
        CancellationToken cancellationToken = default);
}

internal class VmrForwardFlower : VmrCodeflower, IVmrForwardFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrUpdater _vmrUpdater;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitClient _localGitClient;
    private readonly IProcessManager _processManager;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly ILogger<VmrCodeflower> _logger;

    public VmrForwardFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrUpdater vmrUpdater,
        IVmrDependencyTracker dependencyTracker,
        ILocalGitClient localGitClient,
        IVersionDetailsParser versionDetailsParser,
        IProcessManager processManager,
        IWorkBranchFactory workBranchFactory,
        IFileSystem fileSystem,
        ILogger<VmrCodeflower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, versionDetailsParser, processManager, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _vmrUpdater = vmrUpdater;
        _dependencyTracker = dependencyTracker;
        _localGitClient = localGitClient;
        _processManager = processManager;
        _workBranchFactory = workBranchFactory;
        _logger = logger;
    }

    public async Task<string?> FlowForwardAsync(
        string mapping,
        NativePath sourceRepo,
        string? shaToFlow = null,
        CancellationToken cancellationToken = default)
        => await FlowCodeAsync(
            isBackflow: false,
            sourceRepo,
            mapping,
            shaToFlow ?? await _localGitClient.GetShaForRefAsync(sourceRepo, Constants.HEAD),
            cancellationToken);

    // TODO: Docs
    protected override async Task<string?> SameDirectionFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath repoPath,
        Codeflow lastFlow,
        CancellationToken cancellationToken)
    {
        var branchName = $"codeflow/forward/{Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(shaToFlow)}";

        await _workBranchFactory.CreateWorkBranchAsync(repoPath, branchName);

        // TODO: Detect if no changes
        var hadUpdates = await _vmrUpdater.UpdateRepository(
            mapping.Name,
            shaToFlow,
            "1.2.3", // TODO
            updateDependencies: false,
            // TODO - all parameters below should come from BAR build / options
            additionalRemotes: Array.Empty<AdditionalRemote>(),
            readmeTemplatePath: null,
            tpnTemplatePath: null,
            generateCodeowners: true,
            discardPatches: true,
            cancellationToken);

        return hadUpdates ? branchName : null;
    }

    // TODO: Docs
    protected override async Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath repoPath,
        Codeflow lastFlow,
        CancellationToken cancellationToken)
    {
        var shortShas = $"{Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(shaToFlow)}";
        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-backflow-{shortShas}.patch";

        await _localGitClient.CheckoutAsync(repoPath, lastFlow.SourceSha);

        var branchName = $"codeflow/forward/{shortShas}";
        var prBanch = await _workBranchFactory.CreateWorkBranchAsync(repoPath, branchName);
        _logger.LogInformation("Created temporary branch {branchName} in {repoDir}", branchName, repoPath);
        var submodules = await _localGitClient.GetGitSubmodulesAsync(repoPath, shaToFlow);

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to the VMR, we remove all files but sobmodules and cloaked files
        List<string> removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. submodules.Select(s => s.Path).Select(VmrPatchHandler.GetExclusionRule),
        ];

        var result = await _processManager.Execute(
            _processManager.GitExecutable,
            ["rm", "-r", "-q", "--", .. removalFilters],
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            cancellationToken: cancellationToken);

        result.ThrowIfFailed($"Failed to remove files from {repoPath}");

        // We make the VMR believe it has the zero commit of the repo as it matches the dir/git state at the moment
        _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
            mapping,
            repoPath, // TODO = URL from BAR build
            Constants.EmptyGitObject,
            _dependencyTracker.GetDependencyVersion(mapping)!.PackageVersion,
            Parent: null));

        // TODO: Detect if no changes
        var hadUpdates = await _vmrUpdater.UpdateRepository(
            mapping.Name,
            shaToFlow,
            "1.2.3",
            updateDependencies: false,
            // TODO - all parameters below should come from BAR build / options
            additionalRemotes: Array.Empty<AdditionalRemote>(),
            readmeTemplatePath: null,
            tpnTemplatePath: null,
            generateCodeowners: true,
            discardPatches: false,
            cancellationToken);

        return hadUpdates ? branchName : null;
    }
}
