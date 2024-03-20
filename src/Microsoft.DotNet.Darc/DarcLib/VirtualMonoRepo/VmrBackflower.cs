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
    /// <param name="newBranchName">New branch name</param>
    /// <param name="targetBranch">Target branch to create the PR branch on top of</param>
    /// <param name="discardPatches">Keep patch files?</param>
    Task<bool> FlowBackAsync(
        string mapping,
        NativePath targetRepo,
        string? shaToFlow,
        int? buildToFlow,
        string? newBranchName,
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
    /// <param name="newBranchName">New branch name</param>
    /// <param name="targetBranch">Target branch to create the PR branch on top of</param>
    /// <param name="discardPatches">Keep patch files?</param>
    Task<bool> FlowBackAsync(
        string mapping,
        ILocalGitRepo targetRepo,
        string? shaToFlow,
        int? buildToFlow,
        string? newBranchName,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flows backward the code from the VMR to the target branch of a product repo.
    /// This overload is used in the context of the PCS.
    /// </summary>
    /// <param name="mappingName">Mapping to flow</param>
    /// <param name="build">Build to flow</param>
    /// <param name="newBranchName">New branch name</param>
    /// <param name="targetBranch">Target branch to create the PR branch on top of</param>
    /// <returns>
    ///     Boolean whether there were any changes to be flown
    ///     and a path to the local repo where the new branch is created
    ///  </returns>
    Task<(bool HadUpdates, NativePath RepoPath)> FlowBackAsync(
        string mappingName,
        Build build,
        string newBranchName,
        string targetBranch,
        CancellationToken cancellationToken = default);
}

internal class VmrBackFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
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
    : VmrCodeFlower(vmrInfo, sourceManifest, dependencyTracker, repositoryCloneManager, localGitClient, libGit2Client, localGitRepoFactory, versionDetailsParser, dependencyFileManager, coherencyUpdateResolver, assetLocationResolver, fileSystem, logger),
    IVmrBackFlower
{
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly ISourceManifest _sourceManifest = sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker = dependencyTracker;
    private readonly ILocalGitClient _localGitClient = localGitClient;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IVmrPatchHandler _vmrPatchHandler = vmrPatchHandler;
    private readonly IWorkBranchFactory _workBranchFactory = workBranchFactory;
    private readonly IBasicBarClient _barClient = basicBarClient;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly ILogger<VmrCodeFlower> _logger = logger;

    public Task<bool> FlowBackAsync(
        string mapping,
        NativePath targetRepoPath,
        string? shaToFlow,
        int? buildToFlow,
        string? branchName,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
        => FlowBackAsync(
            mapping,
            _localGitRepoFactory.Create(targetRepoPath),
            shaToFlow,
            buildToFlow,
            branchName,
            discardPatches,
            cancellationToken);

    public async Task<(bool HadUpdates, NativePath RepoPath)> FlowBackAsync(
        string mappingName,
        Build build,
        string newBranchName,
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        ILocalGitRepo targetRepo = await PrepareRepoAndVmr(mappingName, targetBranch, build.Commit, cancellationToken);
        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        Codeflow lastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

        var hadUpdates = await FlowBackAsync(
            mapping,
            targetRepo,
            lastFlow,
            build.Commit,
            build,
            newBranchName,
            true,
            cancellationToken);

        return (hadUpdates, targetRepo.Path);
    }

    public async Task<bool> FlowBackAsync(
        string mappingName,
        ILocalGitRepo targetRepo,
        string? shaToFlow,
        int? buildToFlow,
        string? newBranchName,
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
        if (shaToFlow is null)
        {
            shaToFlow = await _localGitClient.GetShaForRefAsync(_vmrInfo.VmrPath);
        }
        else
        {
            await CheckOutVmr(shaToFlow);
        }

        var mapping = _dependencyTracker.GetMapping(mappingName);
        Codeflow lastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);
        return await FlowBackAsync(
            mapping,
            targetRepo,
            lastFlow,
            shaToFlow,
            build,
            newBranchName,
            discardPatches,
            cancellationToken);
    }

    private async Task<bool> FlowBackAsync(
        SourceMapping mapping,
        ILocalGitRepo targetRepo,
        Codeflow lastFlow,
        string shaToFlow,
        Build? build,
        string? newBranchName,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        var hasChanges = await FlowCodeAsync(
            lastFlow,
            new Backflow(lastFlow.TargetSha, shaToFlow),
            targetRepo,
            mapping,
            build,
            newBranchName,
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
        string newBranchName,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        // Exclude all submodules that belong to the mapping
        var submoduleExclusions = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1))
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

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
        await _workBranchFactory.CreateWorkBranchAsync(targetRepo, newBranchName);

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

            // Reconstruct the previous flow's branch
            var lastLastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

            await FlowCodeAsync(
                lastLastFlow,
                new Backflow(lastLastFlow.SourceSha, lastFlow.SourceSha),
                targetRepo,
                mapping,
                /* TODO: Find a previous build? */ null,
                newBranchName,
                discardPatches,
                cancellationToken);

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

        _logger.LogInformation("New branch {branch} with flown code is ready in {repoDir}", newBranchName, targetRepo);

        return true;
    }

    protected override async Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build? build,
        string branchName,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        await targetRepo.CheckoutAsync(lastFlow.SourceSha);

        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";
        var prBanch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName);
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

        return true;
    }
}
