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
        => await PickFlowStrategyAsync(
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
        var isBackflow = lastFlow is Backflow;
        var shortShas = $"{Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(shaToFlow)}";
        var branchName = $"codeflow/{lastFlow.GetType().Name.ToLower()}/{shortShas}";
        var targetRepo = isBackflow ? repoPath : _vmrInfo.VmrPath;

        await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName);

        // TODO: Detect if no changes
        var updated = await _vmrUpdater.UpdateRepository(
            mapping.Name,
            shaToFlow,
            "1.2.3",
            updateDependencies: false,
            // TODO
            additionalRemotes: Array.Empty<AdditionalRemote>(),
            readmeTemplatePath: null,
            tpnTemplatePath: null,
            generateCodeowners: true,
            discardPatches: false,
            cancellationToken);

        return updated ? branchName : null;
    }

    // TODO: Docs
    protected override async Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath repoPath,
        Codeflow lastFlow,
        CancellationToken cancellationToken)
    {
        var isBackflow = lastFlow is ForwardFlow;
        var targetRepo = isBackflow ? repoPath : _vmrInfo.VmrPath;
        var shortShas = $"{Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(shaToFlow)}";
        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{(isBackflow ? "backflow" : "forwardflow")}-{shortShas}.patch";

        await _localGitClient.CheckoutAsync(targetRepo, lastFlow.SourceSha);

        var branchName = $"codeflow/{lastFlow.GetType().Name.ToLower()}/{shortShas}";
        var prBanch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName);
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

        result.ThrowIfFailed($"Failed to remove files from {targetRepo}");

        _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
            mapping,
            repoPath, // TODO
            Constants.EmptyGitObject,
            _dependencyTracker.GetDependencyVersion(mapping)!.PackageVersion,
            null));

        // TODO: Detect if no changes
        var updated = await _vmrUpdater.UpdateRepository(
            mapping.Name,
            shaToFlow,
            "1.2.3",
            updateDependencies: false,
            // TODO
            additionalRemotes: Array.Empty<AdditionalRemote>(),
            readmeTemplatePath: null,
            tpnTemplatePath: null,
            generateCodeowners: true,
            discardPatches: false,
            cancellationToken);

        return updated ? branchName : null;
    }
}
