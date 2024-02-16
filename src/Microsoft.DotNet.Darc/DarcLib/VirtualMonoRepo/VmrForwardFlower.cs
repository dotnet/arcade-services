// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrForwardFlower
{
    Task<string?> FlowForwardAsync(
        string mapping,
        NativePath sourceRepo,
        string? shaToFlow,
        int? buildToFlow,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);
}

internal class VmrForwardFlower : VmrCodeFlower, IVmrForwardFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrUpdater _vmrUpdater;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IBasicBarClient _barClient;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IProcessManager _processManager;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly ILogger<VmrCodeFlower> _logger;

    public VmrForwardFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrUpdater vmrUpdater,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
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
        : base(
            vmrInfo,
            sourceManifest,
            dependencyTracker,
            localGitClient,
            libGit2Client,
            localGitRepoFactory,
            versionDetailsParser,
            dependencyFileManager,
            coherencyUpdateResolver,
            assetLocationResolver,
            fileSystem,
            logger)
    {
        _vmrInfo = vmrInfo;
        _vmrUpdater = vmrUpdater;
        _dependencyTracker = dependencyTracker;
        _barClient = basicBarClient;
        _localGitRepoFactory = localGitRepoFactory;
        _processManager = processManager;
        _workBranchFactory = workBranchFactory;
        _logger = logger;
    }

    public async Task<string?> FlowForwardAsync(
        string mappingName,
        NativePath repoPath,
        string? shaToFlow,
        int? buildToFlow,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
    {
        Build? build = null;
        if (buildToFlow.HasValue)
        {
            build = await _barClient.GetBuildAsync(buildToFlow.Value)
                ?? throw new Exception($"Failed to find build with BAR ID {buildToFlow}");
        }

        var sourceRepo = _localGitRepoFactory.Create(repoPath);

        // SHA comes either directly or from the build or if none supplied, from tip of the repo
        shaToFlow ??= build?.Commit;
        if (shaToFlow is null)
        {
            shaToFlow = await sourceRepo.GetShaForRefAsync();
        }
        else
        {
            await sourceRepo.CheckoutAsync(shaToFlow);
        }

        var mapping = _dependencyTracker.Mappings.First(m => m.Name == mappingName);
        Codeflow lastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: false);

        var branchName = await FlowCodeAsync(
            lastFlow,
            new ForwardFlow(lastFlow.TargetSha, shaToFlow),
            sourceRepo,
            mapping,
            build,
            discardPatches,
            cancellationToken);

        if (branchName != null)
        {
            await UpdateDependenciesAndToolset(repoPath, LocalVmr, build, shaToFlow, updateSourceElement: false, cancellationToken);
        }

        return branchName;
    }

    protected override async Task<string?> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        Build? build,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        var branchName = currentFlow.GetBranchName();
        await _workBranchFactory.CreateWorkBranchAsync(LocalVmr, branchName);

        List<AdditionalRemote> additionalRemotes =
        [
            new AdditionalRemote(mapping.Name, sourceRepo.Path)
        ];

        if (build is not null)
        {
            additionalRemotes.Add(new AdditionalRemote(mapping.Name, build.GetRepository()));
        }

        bool hadUpdates;

        try
        {
            // If the build produced any assets, we use the number to update VMR's git info files
            // The git info files won't be important by then and probably removed but let's keep it for now
            string? targetVersion = null;
            if (build?.Assets.Count > 0)
            {
                targetVersion = build?.Assets[0].Version;
            }

            hadUpdates = await _vmrUpdater.UpdateRepository(
                mapping.Name,
                currentFlow.TargetSha,
                targetVersion,
                updateDependencies: false,
                additionalRemotes: additionalRemotes,
                componentTemplatePath: null,
                tpnTemplatePath: null,
                generateCodeowners: true,
                discardPatches,
                cancellationToken);
        }
        catch (Exception e) when (e.Message.Contains("Failed to apply the patch"))
        {
            // TODO https://github.com/dotnet/arcade-services/issues/2995: This can happen when we also update a PR branch but there are conflicting changes inside.
            // In this case, we should just stop. We need a flag for that.

            // This happens when a conflicting change was made in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#conflicts
            _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating the previous flow..");

            // Find the last target commit in the repo
            var previousFlowTargetSha = await BlameLineAsync(
                _vmrInfo.SourceManifestPath,
                line => line.Contains(lastFlow.SourceSha),
                lastFlow.TargetSha);
            await CheckOutVmr(previousFlowTargetSha);

            // Reconstruct the previous flow's branch
            var lastLastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: true);
            branchName = await FlowCodeAsync(
                lastLastFlow,
                new ForwardFlow(lastLastFlow.SourceSha, lastFlow.SourceSha),
                sourceRepo,
                mapping,
                null, // TODO: This is an interesting one - should we try to find a build for that previous SHA?
                discardPatches,
                cancellationToken);

            // We apply the current changes on top again - they should apply now
            // TODO https://github.com/dotnet/arcade-services/issues/2995: Handle exceptions
            hadUpdates = await _vmrUpdater.UpdateRepository(
                mapping.Name,
                currentFlow.TargetSha,
                // TODO - all parameters below should come from BAR build / options
                "1.2.3",
                updateDependencies: false,
                additionalRemotes,
                componentTemplatePath: null,
                tpnTemplatePath: null,
                generateCodeowners: false,
                discardPatches,
                cancellationToken);
        }

        return hadUpdates ? branchName : null;
    }

    protected override async Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        Build? build,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        await sourceRepo.CheckoutAsync(lastFlow.TargetSha);

        var branchName = currentFlow.GetBranchName();
        var patchName = _vmrInfo.TmpPath / $"{branchName.Replace('/', '-')}.patch";
        var prBanch = await _workBranchFactory.CreateWorkBranchAsync(LocalVmr, branchName);

        List<GitSubmoduleInfo> submodules =
        [
            .. await sourceRepo.GetGitSubmodulesAsync(lastFlow.RepoSha),
            .. await sourceRepo.GetGitSubmodulesAsync(currentFlow.TargetSha),
        ];

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to the VMR, we remove all files but sobmodules and cloaked files
        List<string> removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. submodules.Select(s => s.Path).Distinct().Select(VmrPatchHandler.GetExclusionRule),
        ];

        var result = await _processManager.Execute(
            _processManager.GitExecutable,
            ["rm", "-r", "-q", "--", .. removalFilters],
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            cancellationToken: cancellationToken);

        result.ThrowIfFailed($"Failed to remove files from {sourceRepo}");

        // We make the VMR believe it has the zero commit of the repo as it matches the dir/git state at the moment
        _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
            mapping,
            sourceRepo.Path, // TODO = URL from BAR build
            Constants.EmptyGitObject,
            _dependencyTracker.GetDependencyVersion(mapping)!.PackageVersion,
            Parent: null));

        IReadOnlyCollection<AdditionalRemote>? additionalRemote = build is not null
            ? [new AdditionalRemote(mapping.Name, build.GetRepository())]
            : [];

        string? targetVersion = null;
        if (build?.Assets.Count > 0)
        {
            targetVersion = build.Assets[0].Version;
        }

        // TODO: Detect if no changes
        var hadUpdates = await _vmrUpdater.UpdateRepository(
            mapping.Name,
            currentFlow.TargetSha,
            targetVersion,
            updateDependencies: false,
            additionalRemote,
            componentTemplatePath: null,
            tpnTemplatePath: null,
            generateCodeowners: false,
            discardPatches,
            cancellationToken);

        // TODO: Technically, if we only changed metadata files, there are no updates still
        return hadUpdates ? branchName : null;
    }
}
