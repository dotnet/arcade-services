// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

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
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly IRepositoryCloneManager _repositoryCloneManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly ILocalLibGit2Client _libGit2Client;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IAssetLocationResolver _assetLocationResolver;
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
            ILocalLibGit2Client libGit2Client,
            ICoherencyUpdateResolver coherencyUpdateResolver,
            IAssetLocationResolver assetLocationResolver,
            IFileSystem fileSystem,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, localGitRepoFactory, versionDetailsParser, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _dependencyFileManager = dependencyFileManager;
        _vmrCloneManager = vmrCloneManager;
        _repositoryCloneManager = repositoryCloneManager;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _vmrPatchHandler = vmrPatchHandler;
        _workBranchFactory = workBranchFactory;
        _libGit2Client = libGit2Client;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _assetLocationResolver = assetLocationResolver;
        _fileSystem = fileSystem;
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

        hasChanges |= await UpdateDependenciesAndToolset(
            _vmrInfo.VmrPath,
            targetRepo,
            build,
            excludedAssets,
            hadPreviousChanges: hasChanges,
            cancellationToken);

        if (hasChanges)
        {
            // We try to merge the target branch so that we can potentially
            // resolve some expected conflicts in the version files
            await TryMergingBranch(
                mapping.Name,
                targetRepo,
                build,
                excludedAssets,
                headBranch,
                targetBranch,
                cancellationToken);
        }

        return hasChanges;
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
            currentFlow.VmrSha,
            path: null,
            filters: submoduleExclusions,
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
                throw new ConflictInPrBranchException(e.Result, targetBranch, isForwardFlow: false);
            }

            // Otherwise, we have a conflicting change in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#conflicts
            _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating the previous flow..");

            // Find the last target commit in the repo
            var previousRepoSha = await BlameLineAsync(
                targetRepo.Path / VersionFiles.VersionDetailsXml,
                line => line.Contains(VersionDetailsParser.SourceElementName) && line.Contains(lastFlow.SourceSha),
                lastFlow.RepoSha);
            await targetRepo.CheckoutAsync(previousRepoSha);
            await targetRepo.CreateBranchAsync(headBranch, overwriteExistingBranch: true);

            // Reconstruct the previous flow's branch
            var lastLastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

            await FlowCodeAsync(
                lastLastFlow,
                lastFlow,
                targetRepo,
                mapping,
                // TODO (https://github.com/dotnet/arcade-services/issues/4166): Find a previous build?
                new Build(-1, DateTimeOffset.Now, 0, false, false, lastLastFlow.VmrSha, [], [], [], []),
                excludedAssets,
                headBranch,
                headBranch,
                discardPatches,
                headBranchExisted,
                cancellationToken);

            // The recursive call right above would returned checked out at targetBranch
            // The original work branch from above is no longer relevant. We need to create it again
            workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, newBranchName, headBranch);

            // The current patches should apply now
            foreach (VmrIngestionPatch patch in patches)
            {
                // TODO https://github.com/dotnet/arcade-services/issues/2995: Catch exceptions?
                await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, reverseApply: false, cancellationToken);
            }
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
        CancellationToken cancellationToken)
    {
        await targetRepo.CheckoutAsync(lastFlow.SourceSha);

        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}.patch";
        var branchName = currentFlow.GetBranchName();
        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName, headBranch);
        _logger.LogInformation("Created temporary branch {branchName} in {repoDir}", branchName, targetRepo);

        // We leave the inlined submodules in the VMR
        var submoduleExclusions = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1))
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            Constants.EmptyGitObject,
            currentFlow.VmrSha,
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
            [VMR] Codeflow {Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentFlow.VmrSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await targetRepo.CommitAsync(commitMessage, false, cancellationToken: cancellationToken);
        await targetRepo.ResetWorkingTree();
        await workBranch.MergeBackAsync(commitMessage);

        return true;
    }

    /// <summary>
    /// This is a naive implementation of conflict resolution for version files.
    /// It takes the versions from the target branch and updates it with current build's assets.
    /// This means that any changes to the version files done in the VMR will be lost.
    /// See https://github.com/dotnet/arcade-services/issues/4342 for more information
    /// </summary>
    protected override async Task<bool> TryResolveConflicts(
        string mappingName,
        ILocalGitRepo repo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        string targetBranch,
        IEnumerable<UnixPath> conflictedFiles,
        CancellationToken cancellationToken)
    {
        var result = await repo.RunGitCommandAsync(["checkout", "--theirs", "."], cancellationToken);
        result.ThrowIfFailed("Failed to check out the conflicted files");

        await UpdateDependenciesAndToolset(
            _vmrInfo.VmrPath,
            repo,
            build,
            excludedAssets,
            false,
            cancellationToken);

        await repo.StageAsync(["."], cancellationToken);

        _logger.LogInformation("Auto-resolved conflicts in version files");
        return true;
    }

    protected override Task<bool> TryResolvingConflict(
            string mappingName,
            ILocalGitRepo repo,
            Build build,
            string filePath,
            CancellationToken cancellationToken)
        => throw new NotImplementedException(); // We don't need to resolve individual files as we handle all together

    protected override IEnumerable<UnixPath> GetAllowedConflicts(IEnumerable<UnixPath> conflictedFiles, string mappingName)
    {
        var allowedVersionFiles = DependencyFileManager.DependencyFiles
            .Select(f => f.ToLowerInvariant())
            .ToList();

        return conflictedFiles
            .Where(f => f.Path.ToLowerInvariant().StartsWith(Constants.CommonScriptFilesPath + '/')
                        || allowedVersionFiles.Contains(f.Path.ToLowerInvariant()));
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
        };
    }

    /// <summary>
    /// Updates version details, eng/common and other version files (global.json, ...) based on a build that is being flown.
    /// For backflows, updates the Source element in Version.Details.xml.
    /// </summary>
    /// <param name="sourceRepo">Source repository (needed when eng/common is flown too)</param>
    /// <param name="targetRepo">Target repository directory</param>
    /// <param name="build">Build with assets (dependencies) that is being flows</param>
    /// <param name="excludedAssets">Assets to exclude from the dependency flow</param>
    /// <param name="hadPreviousChanges">Set to true when we already had a code flow commit to amend the dependency update into it</param>
    private async Task<bool> UpdateDependenciesAndToolset(
        NativePath sourceRepo,
        ILocalGitRepo targetRepo,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        bool hadPreviousChanges,
        CancellationToken cancellationToken)
    {
        VersionDetails versionDetails = _versionDetailsParser.ParseVersionDetailsFile(targetRepo.Path / VersionFiles.VersionDetailsXml);
        await _assetLocationResolver.AddAssetLocationToDependenciesAsync(versionDetails.Dependencies);

        List<DependencyUpdate> updates;
        bool hadUpdates = false;

        var sourceOrigin = new SourceDependency(
            build.GetRepository(),
            build.Commit,
            build.Id);

        if (versionDetails.Source?.Sha != sourceOrigin.Sha || versionDetails.Source?.BarId != sourceOrigin.BarId)
        {
            hadUpdates = true;
        }

        // Generate the <Source /> element and get updates
        if (build is not null)
        {
            IEnumerable<AssetData> assetData = build.Assets
                .Where(a => excludedAssets is null || !excludedAssets.Contains(a.Name))
                .Select(a => new AssetData(a.NonShipping)
                {
                    Name = a.Name,
                    Version = a.Version
                });

            updates = _coherencyUpdateResolver.GetRequiredNonCoherencyUpdates(
                build.GetRepository() ?? Constants.DefaultVmrUri,
                build.Commit,
                assetData,
                versionDetails.Dependencies);

            await _assetLocationResolver.AddAssetLocationToDependenciesAsync([.. updates.Select(u => u.To)]);
        }
        else
        {
            updates = [];
        }

        // If we are updating the arcade sdk we need to update the eng/common files as well
        DependencyDetail? arcadeItem = updates.GetArcadeUpdate();
        SemanticVersion? targetDotNetVersion = null;

        if (arcadeItem != null)
        {
            targetDotNetVersion = await _dependencyFileManager.ReadToolsDotnetVersionAsync(arcadeItem.RepoUri, arcadeItem.Commit, repoIsVmr: true);
        }

        GitFileContentContainer updatedFiles = await _dependencyFileManager.UpdateDependencyFiles(
            updates.Select(u => u.To),
            sourceOrigin,
            targetRepo.Path,
            branch: null, // reads the working tree
            versionDetails.Dependencies,
            targetDotNetVersion);

        // This actually does not commit but stages only
        await _libGit2Client.CommitFilesAsync(updatedFiles.GetFilesToCommit(), targetRepo.Path, null, null);

        // Update eng/common files
        if (arcadeItem != null)
        {
            // Check if the VMR contains src/arcade/eng/common
            var arcadeEngCommonDir = GetEngCommonPath(sourceRepo);
            if (!_fileSystem.DirectoryExists(arcadeEngCommonDir))
            {
                _logger.LogWarning("VMR does not contain src/arcade/eng/common, skipping eng/common update");
                return hadUpdates;
            }

            var commonDir = targetRepo.Path / Constants.CommonScriptFilesPath;
            if (_fileSystem.DirectoryExists(commonDir))
            {
                _fileSystem.DeleteDirectory(commonDir, true);
            }

            _fileSystem.CopyDirectory(
                arcadeEngCommonDir,
                targetRepo.Path / Constants.CommonScriptFilesPath,
                true);
        }

        if (!await targetRepo.HasWorkingTreeChangesAsync())
        {
            return hadUpdates;
        }

        await targetRepo.StageAsync(["."], cancellationToken);

        if (hadPreviousChanges)
        {
            await targetRepo.CommitAmendAsync(cancellationToken);
        }
        else
        {
            await targetRepo.CommitAsync("Updated dependencies", allowEmpty: true, cancellationToken: cancellationToken);
        }

        return true;
    }

    protected override NativePath GetEngCommonPath(NativePath sourceRepo) => sourceRepo / VmrInfo.ArcadeRepoDir / Constants.CommonScriptFilesPath;
    protected override bool TargetRepoIsVmr() => false;
    // During backflow, we're flowing a specific VMR commit that the build was built from, so we should just check it out
    protected virtual bool ShouldResetVmr => false;
}
