﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
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
    /// <param name="buildToFlow">Build to flow</param>
    /// <param name="excludedAssets">Assets to exclude from the dependency flow</param>
    /// <param name="targetBranch">Target branch to create the PR against. If target branch does not exist, it is created off of this branch</param>
    /// <param name="headBranch">New/existing branch to make the changes on</param>
    /// <param name="discardPatches">Keep patch files?</param>
    Task<bool> FlowBackAsync(
        string mapping,
        NativePath targetRepo,
        Build buildToFlow,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
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
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IVersionFileCodeFlowUpdater _versionFileConflictResolver;
    private readonly IFileSystem _fileSystem;
    private readonly IBasicBarClient _barClient;
    private readonly ILogger<VmrCodeFlower> _logger;

    public VmrBackFlower(
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
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, localGitRepoFactory, versionDetailsParser, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _vmrCloneManager = vmrCloneManager;
        _repositoryCloneManager = repositoryCloneManager;
        _localGitClient = localGitClient;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _vmrPatchHandler = vmrPatchHandler;
        _workBranchFactory = workBranchFactory;
        _versionFileConflictResolver = versionFileConflictResolver;
        _fileSystem = fileSystem;
        _barClient = barClient;
        _logger = logger;
    }

    public async Task<bool> FlowBackAsync(
        string mappingName,
        NativePath targetRepoPath,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
    {
        var targetRepo = _localGitRepoFactory.Create(targetRepoPath);
        (bool headBranchExisted, SourceMapping mapping) = await PrepareVmrAndRepo(
            mappingName,
            targetRepo,
            build,
            targetBranch,
            headBranch,
            cancellationToken);

        Codeflow lastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

        return await FlowBackAsync(
            mapping,
            targetRepo,
            lastFlow,
            build,
            excludedAssets,
            targetBranch,
            headBranch,
            discardPatches,
            headBranchExisted,
            cancellationToken);
    }

    protected async Task<bool> FlowBackAsync(
        SourceMapping mapping,
        ILocalGitRepo targetRepo,
        Codeflow lastFlow,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var currentFlow = new Backflow(build.Commit, lastFlow.RepoSha);
        var hasChanges = await FlowCodeAsync(
            lastFlow,
            currentFlow,
            targetRepo,
            mapping,
            build,
            excludedAssets,
            targetBranch,
            headBranch,
            discardPatches,
            headBranchExisted,
            cancellationToken);

        // We try to merge the target branch and we apply dependency updates
        List<DependencyUpdate> dependencyUpdates = await _versionFileConflictResolver.TryMergingBranchAndUpdateDependencies(
            mapping,
            lastFlow,
            currentFlow,
            targetRepo,
            build,
            headBranch,
            targetBranch,
            excludedAssets,
            headBranchExisted,
            cancellationToken);

        return hasChanges || dependencyUpdates.Any();
    }

    protected override async Task<bool> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        string headBranch,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        string newBranchName = currentFlow.GetBranchName();
        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";

        // When flowing from the VMR, ignore all submodules
        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            lastFlow.VmrSha,
            currentFlow.VmrSha,
            path: null,
            filters: GetPatchExclusions(mapping),
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            applicationPath: null,
            cancellationToken);

        if (patches.Count == 0 || patches.All(p => _fileSystem.GetFileInfo(p.Path).Length == 0))
        {
            _logger.LogInformation("There are no new changes for VMR between {sha1} and {sha2}",
                lastFlow.VmrSha,
                currentFlow.VmrSha);

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

        var workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, newBranchName, headBranch);

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

            // When we are updating an already existing PR branch, there can be conflicting changes in the PR from devs.
            // In that case we want to throw as that is a conflict we don't want to try to resolve.
            if (headBranchExisted)
            {
                _logger.LogInformation("Failed to update a PR branch because of a conflict. Stopping the flow..");
                throw new ConflictInPrBranchException(e.Result, targetBranch, mapping.Name, isForwardFlow: false);
            }

            // Otherwise, we have a conflicting change in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#conflicts
            await RecreatePreviousFlowAndApplyBuild(
                mapping,
                targetRepo,
                lastFlow,
                headBranch,
                newBranchName,
                excludedAssets,
                patches,
                discardPatches,
                headBranchExisted,
                cancellationToken);
        }

        var commitMessage = $"""
            [VMR] Codeflow {Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.VmrSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await targetRepo.CommitAsync(commitMessage, allowEmpty: false, cancellationToken: cancellationToken);
        await targetRepo.ResetWorkingTree();
        await workBranch.MergeBackAsync(commitMessage);

        _logger.LogInformation("Branch {branch} with code changes is ready in {repoDir}", headBranch, targetRepo);

        return true;
    }

    protected override async Task<bool> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build build,
        string targetBranch,
        string headBranch,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        await targetRepo.CheckoutAsync(lastFlow.RepoSha);

        // If the target branch did not exist, we need to make sure it is created in the right location
        if (!headBranchExisted)
        {
            await targetRepo.CreateBranchAsync(headBranch, true);
        }

        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";
        var branchName = currentFlow.GetBranchName();
        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName, headBranch);
        _logger.LogInformation("Created temporary branch {branchName} in {repoDir}", branchName, targetRepo);

        // We leave the inlined submodules in the VMR
        var exclusions = GetPatchExclusions(mapping);

        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            Constants.EmptyGitObject,
            currentFlow.VmrSha,
            path: null,
            filters: exclusions,
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
            .. exclusions,
        ];

        string[] args = ["rm", "-r", "-q"];
        if (removalFilters.Count > 0)
        {
            args = [.. args, "--", .. removalFilters];
        }
        else
        {
            args = [.. args, "."];
        }

        ProcessExecutionResult result = await targetRepo.ExecuteGitCommand(args, cancellationToken);
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
            // When no changes happened, we disregard the work branch and return back to the target branch
            await targetRepo.CheckoutAsync(headBranch);
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

    private async Task<(bool, SourceMapping)> PrepareVmrAndRepo(
        string mappingName,
        ILocalGitRepo targetRepo,
        Build build,
        string targetBranch,
        string headBranch,
        CancellationToken cancellationToken)
    {
        await _vmrCloneManager.PrepareVmrAsync([build.GetRepository()], [build.Commit], build.Commit, ShouldResetVmr, cancellationToken);

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
                [targetBranch, headBranch],
                headBranch,
                ShouldResetVmr,
                cancellationToken);
            return (true, mapping);
        }
        catch (NotFoundException)
        {
            // If target branch does not exist, we create it off of the base branch
            await targetRepo.CheckoutAsync(targetBranch);
            await targetRepo.CreateBranchAsync(headBranch);
            return (false, mapping);
        }
    }

    private IReadOnlyCollection<string> GetPatchExclusions(SourceMapping mapping) =>
        // Exclude all submodules that belong to the mapping
        [.._sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1))

        // Exclude version files as those will be handled manually
        .Concat(DependencyFileManager.DependencyFiles)

        // Exclude eng/common as that will be copied based on the arcade version
        .Append(Constants.CommonScriptFilesPath)
        .Select(VmrPatchHandler.GetExclusionRule)];

    private async Task RecreatePreviousFlowAndApplyBuild(
        SourceMapping mapping,
        ILocalGitRepo targetRepo,
        Codeflow lastFlow,
        string headBranch,
        string newBranchName,
        IReadOnlyCollection<string>? excludedAssets,
        List<VmrIngestionPatch> patches,
        bool discardPatches,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating the previous flow..");

        // Find the last target commit in the repo
        var previousRepoSha = await _localGitClient.BlameLineAsync(
            targetRepo.Path / VersionFiles.VersionDetailsXml,
            line => line.Contains(VersionDetailsParser.SourceElementName) && line.Contains(lastFlow.SourceSha),
            lastFlow.RepoSha);

        // Find the ID of the last VMR build that flowed into the repo
        VersionDetails versionDetails = _versionDetailsParser.ParseVersionDetailsFile(targetRepo.Path / VersionFiles.VersionDetailsXml);

        // checkout the previous repo sha so we can get the last last flow
        await targetRepo.CheckoutAsync(previousRepoSha);
        await targetRepo.CreateBranchAsync(headBranch, overwriteExistingBranch: true);
        var lastLastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

        Build previouslyAppliedVmrBuild;
        if (versionDetails.Source?.BarId != null)
        {
            previouslyAppliedVmrBuild = await _barClient.GetBuildAsync(versionDetails.Source.BarId.Value);
        }
        else
        {
            // If we don't find the previously applied build, there probably wasn't one.
            // In this case, we won't update assets, but that's ok because they'll get overwritten by the new build anyway
            previouslyAppliedVmrBuild = new(-1, DateTimeOffset.Now, 0, false, false, lastLastFlow.VmrSha, [], [], [], []);
        }

        // Reconstruct the previous flow's branch
        await FlowCodeAsync(
            lastLastFlow,
            lastFlow,
            targetRepo,
            mapping,
            previouslyAppliedVmrBuild,
            excludedAssets,
            headBranch,
            headBranch,
            discardPatches,
            headBranchExisted,
            cancellationToken);

        // The recursive call right above would returned checked out at targetBranch
        // The original work branch from above is no longer relevant. We need to create it again
        var workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, newBranchName, headBranch);

        // The current patches should apply now
        foreach (VmrIngestionPatch patch in patches)
        {
            // TODO https://github.com/dotnet/arcade-services/issues/2995: Catch exceptions?
            await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, reverseApply: false, cancellationToken);
        }
    }

    protected override NativePath GetEngCommonPath(NativePath sourceRepo) => sourceRepo / VmrInfo.ArcadeRepoDir / Constants.CommonScriptFilesPath;
    protected override bool TargetRepoIsVmr() => false;
    // During backflow, we're flowing a specific VMR commit that the build was built from, so we should just check it out
    protected virtual bool ShouldResetVmr => false;
}
