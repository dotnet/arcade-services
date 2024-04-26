// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
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

public interface IVmrBackFlower
{
    /// <summary>
    /// Flows backward the code from the VMR to the target branch of a product repo.
    /// This overload is used in the context of the darc CLI.
    /// </summary>
    /// <param name="mapping">Mapping to flow</param>
    /// <param name="targetRepo">Local checkout of the repository</param>
    /// <param name="shaToFlow">SHA to flow</param>
    /// <param name="buildToFlow">Build to flow</param>
    /// <param name="baseBranch">If target branch does not exist, it is created off of this branch</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <param name="discardPatches">Keep patch files?</param>
    Task<bool> FlowBackAsync(
        string mapping,
        NativePath targetRepo,
        string? shaToFlow,
        int? buildToFlow,
        string baseBranch,
        string targetBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flows backward the code from the VMR to the target branch of a product repo.
    /// This overload is used in the context of the darc CLI.
    /// </summary>
    /// <param name="mapping">Mapping to flow</param>
    /// <param name="targetRepo">Local checkout of the repository</param>
    /// <param name="shaToFlow">SHA to flow</param>
    /// <param name="buildToFlow">Build to flow</param>
    /// <param name="baseBranch">If target branch does not exist, it is created off of this branch</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <param name="discardPatches">Keep patch files?</param>
    Task<bool> FlowBackAsync(
        string mapping,
        ILocalGitRepo targetRepo,
        string? shaToFlow,
        int? buildToFlow,
        string baseBranch,
        string targetBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);
}

internal class VmrBackFlower : VmrCodeFlower, IVmrBackFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly IRepositoryCloneManager _repositoryCloneManager;
    private readonly ILocalGitClient _localGitClient;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IBasicBarClient _barClient;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeFlower> _logger;

    public VmrBackFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            IDependencyFileManager dependencyFileManager,
            IVmrCloneManager vmrCloneManager,
            IRepositoryCloneManager repositoryCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler vmrPatchHandler,
            IWorkBranchFactory workBranchFactory,
            IBasicBarClient basicBarClient,
            ILocalLibGit2Client libGit2Client,
            ICoherencyUpdateResolver coherencyUpdateResolver,
            IAssetLocationResolver assetLocationResolver,
            IFileSystem fileSystem,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, libGit2Client, localGitRepoFactory, versionDetailsParser, dependencyFileManager, coherencyUpdateResolver, assetLocationResolver, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _vmrCloneManager = vmrCloneManager;
        _repositoryCloneManager = repositoryCloneManager;
        _localGitClient = localGitClient;
        _localGitRepoFactory = localGitRepoFactory;
        _vmrPatchHandler = vmrPatchHandler;
        _workBranchFactory = workBranchFactory;
        _barClient = basicBarClient;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public Task<bool> FlowBackAsync(
        string mapping,
        NativePath targetRepoPath,
        string? shaToFlow,
        int? buildToFlow,
        string baseBranch,
        string targetBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
        => FlowBackAsync(
            mapping,
            _localGitRepoFactory.Create(targetRepoPath),
            shaToFlow,
            buildToFlow,
            baseBranch,
            targetBranch,
            discardPatches,
            cancellationToken);

    public async Task<bool> FlowBackAsync(
        string mappingName,
        ILocalGitRepo targetRepo,
        string? shaToFlow,
        int? buildToFlow,
        string baseBranch,
        string targetBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
    {
        Build? build = null;
        if (buildToFlow.HasValue)
        {
            build = await _barClient.GetBuildAsync(buildToFlow.Value)
                ?? throw new Exception($"Failed to find build with BAR ID {buildToFlow}");
        }

        // SHA comes either directly or from the build or if none supplied, from tip of the VMR
        shaToFlow ??= build?.Commit;
        (SourceMapping mapping, shaToFlow) = await PrepareVmrAndRepo(
            mappingName,
            targetRepo,
            shaToFlow,
            baseBranch,
            targetBranch,
            cancellationToken);

        Codeflow lastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);
        return await FlowBackAsync(
            mapping,
            targetRepo,
            lastFlow,
            shaToFlow,
            build,
            baseBranch,
            targetBranch,
            discardPatches,
            cancellationToken);
    }

    protected async Task<bool> FlowBackAsync(
        SourceMapping mapping,
        ILocalGitRepo targetRepo,
        Codeflow lastFlow,
        string shaToFlow,
        Build? build,
        string baseBranch,
        string targetBranch,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        var hasChanges = await FlowCodeAsync(
            lastFlow,
            new Backflow(lastFlow.TargetSha, shaToFlow),
            targetRepo,
            mapping,
            build,
            baseBranch,
            targetBranch,
            discardPatches,
            cancellationToken);

        if (!hasChanges && build is null)
        {
            // TODO https://github.com/dotnet/arcade-services/issues/3168: We should still probably update package versions or at least try?
            // Should we clean up the repos?
            return false;
        }

        await UpdateDependenciesAndToolset(_vmrInfo.VmrPath, targetRepo, build, shaToFlow, updateSourceElement: true, cancellationToken);

        return true;
    }

    protected override async Task<bool> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build? build,
        string baseBranch,
        string targetBranch,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        // Exclude all submodules that belong to the mapping
        var submoduleExclusions = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1))
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

        string newBranchName = currentFlow.GetBranchName();
        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";

        // When flowing from the VMR, ignore all submodules
        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            lastFlow.VmrSha,
            currentFlow.TargetSha,
            path: null,
            filters: submoduleExclusions,
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            applicationPath: null,
            cancellationToken);

        if (patches.Count == 0 || patches.All(p => _fileSystem.GetFileInfo(p.Path).Length == 0))
        {
            _logger.LogInformation("There are no new changes for VMR between {sha1} and {sha2}",
                lastFlow.SourceSha,
                currentFlow.TargetSha);

            if (discardPatches)
            {
                foreach (VmrIngestionPatch patch in patches)
                {
                    _fileSystem.DeleteFile(patch.Path);
                }
            }

            return false;
        }

        _logger.LogInformation("Created {count} patch(es)", patches.Count);

        await targetRepo.CheckoutAsync(lastFlow.TargetSha);
        var workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, newBranchName, targetBranch);

        // TODO https://github.com/dotnet/arcade-services/issues/3302: Remove VMR patches before we create the patches

        try
        {
            foreach (VmrIngestionPatch patch in patches)
            {
                await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, reverseApply: false, cancellationToken);
            }
        }
        catch (PatchApplicationFailedException e)
        {
            _logger.LogInformation(e.Message);

            // TODO https://github.com/dotnet/arcade-services/issues/2995: This can happen when we also update a PR branch but there are conflicting changes inside. In this case, we should just stop. We need a flag for that.

            // This happens when a conflicting change was made in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#conflicts
            _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating the previous flow..");

            // Find the last target commit in the repo
            var previousRepoSha = await BlameLineAsync(
                targetRepo.Path / VersionFiles.VersionDetailsXml,
                line => line.Contains(VersionDetailsParser.SourceElementName) && line.Contains(lastFlow.SourceSha),
                lastFlow.TargetSha);
            await targetRepo.CheckoutAsync(previousRepoSha);
            await targetRepo.CreateBranchAsync(targetBranch, overwriteExistingBranch: true);

            // Reconstruct the previous flow's branch
            var lastLastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

            await FlowCodeAsync(
                lastLastFlow,
                new Backflow(lastLastFlow.SourceSha, lastFlow.SourceSha),
                targetRepo,
                mapping,
                /* TODO: Find a previous build? */ null,
                targetBranch,
                targetBranch,
                discardPatches,
                cancellationToken);

            // The recursive call right above would returned checked out at targetBranch
            // The original work branch from above is no longer relevant. We need to create it again
            workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, newBranchName, targetBranch);

            // The current patches should apply now
            foreach (VmrIngestionPatch patch in patches)
            {
                // TODO https://github.com/dotnet/arcade-services/issues/2995: Catch exceptions?
                await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, reverseApply: false, cancellationToken);
            }
        }

        var commitMessage = $"""
            [VMR] Codeflow {Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await targetRepo.CommitAsync(commitMessage, allowEmpty: false, cancellationToken: cancellationToken);
        await targetRepo.ResetWorkingTree();
        await workBranch.MergeBackAsync(commitMessage);

        _logger.LogInformation("Branch {branch} with code changes is ready in {repoDir}", targetBranch, targetRepo);

        return true;
    }

    protected override async Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build? build,
        string baseBranch,
        string targetBranch,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        await targetRepo.CheckoutAsync(lastFlow.SourceSha);

        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";
        var branchName = currentFlow.GetBranchName();
        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName, targetBranch);
        _logger.LogInformation("Created temporary branch {branchName} in {repoDir}", branchName, targetRepo);

        // We leave the inlined submodules in the VMR
        var submoduleExclusions = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1))
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

        // TODO https://github.com/dotnet/arcade-services/issues/3302: Remove VMR patches before we create the patches

        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            Constants.EmptyGitObject,
            currentFlow.TargetSha,
            path: null,
            submoduleExclusions,
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            applicationPath: null,
            cancellationToken);

        _logger.LogInformation("Created {count} patch(es)", patches.Count);

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to a repo, we remove all repo files but submodules and cloaked files
        List<string> removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. submoduleExclusions,
        ];

        ProcessExecutionResult result = await targetRepo.ExecuteGitCommand(["rm", "-r", "-q", "--", .. removalFilters], cancellationToken);
        result.ThrowIfFailed($"Failed to remove files from {targetRepo}");

        // Now we insert the VMR files
        foreach (var patch in patches)
        {
            // TODO https://github.com/dotnet/arcade-services/issues/2995: Handle exceptions
            await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, reverseApply: false, cancellationToken);
        }

        // TODO https://github.com/dotnet/arcade-services/issues/2995: Check if there are any changes and only commit if there are
        result = await targetRepo.ExecuteGitCommand(["diff-index", "--quiet", "--cached", "HEAD", "--"], cancellationToken);

        if (result.ExitCode == 0)
        {
            // TODO https://github.com/dotnet/arcade-services/issues/2995: Handle + clean up the work branch
            return false;
        }

        var commitMessage = $"""
            [VMR] Codeflow {Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await targetRepo.CommitAsync(commitMessage, false, cancellationToken: cancellationToken);
        await targetRepo.ResetWorkingTree();
        await workBranch.MergeBackAsync(commitMessage);

        return true;
    }

    private async Task<(SourceMapping, string)> PrepareVmrAndRepo(
        string mappingName,
        ILocalGitRepo targetRepo,
        string? shaToFlow,
        string baseBranch,
        string targetBranch,
        CancellationToken cancellationToken)
    {
        if (shaToFlow is null)
        {
            shaToFlow = await _localGitClient.GetShaForRefAsync(_vmrInfo.VmrPath);
        }
        else
        {
            await _vmrCloneManager.PrepareVmrAsync(shaToFlow, CancellationToken.None);
        }

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ISourceComponent repoInfo = _sourceManifest.GetRepoVersion(mappingName);

        var remotes = new[] { mapping.DefaultRemote, repoInfo.RemoteUri }
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToArray();

        // Refresh the repo
        await targetRepo.FetchAllAsync(remotes, cancellationToken);

        try
        {
            // Try to see if both base and target branch are available
            await _repositoryCloneManager.PrepareCloneAsync(
                mapping,
                remotes,
                [baseBranch, targetBranch],
                targetBranch,
                cancellationToken);
        }
        catch (NotFoundException)
        {
            // If target branch does not exist, we create it off of the base branch
            await targetRepo.CheckoutAsync(baseBranch);
            await targetRepo.CreateBranchAsync(targetBranch);
        };

        return (mapping, shaToFlow);
    }
}
