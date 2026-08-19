// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IForwardFlowConflictResolver
{
    /// <summary>
    /// Tries to resolve well-known conflicts that can occur during a code flow operation.
    /// The conflicts can happen when backward a forward flow PRs get merged out of order.
    /// This can be shown on the following schema (the order of events is numbered):
    /// 
    ///     repo                   VMR
    ///       O────────────────────►O
    ///       │  2.                 │ 1.
    ///       │   O◄────────────────O- - ┐
    ///       │   │            4.   │
    ///     3.O───┼────────────►O   │    │
    ///       │   │             │   │
    ///       │ ┌─┘             │   │    │
    ///       │ │               │   │
    ///     5.O◄┘               └──►O 6. │
    ///       │                 7.  │    O (actual branch for 7. is based on top of 1.)
    ///       |────────────────►O   │
    ///       │                 └──►O 8.
    ///       │                     │
    ///
    /// The conflict arises in step 8. and is caused by the fact that:
    ///   - When the forward flow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
    ///   - This means that the PR branch will be based on 1. (the real PR branch is the "actual 7.")
    ///   - This means that when 6. merged, VMR's source-manifest.json got updated with the SHA of the 3.
    ///   - So the source-manifest in 6. contains the SHA of 3.
    ///   - The forward flow PR branch contains the SHA of 5.
    ///   - So the source-manifest file conflicts on the SHA (3. vs 5.)
    ///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
    ///   - This is because basically we know we want to set the version files to point at 5.
    /// </summary>
    /// <returns>Conflicted files (if any)</returns>
    Task<IReadOnlyCollection<UnixPath>> TryMergingBranchAndUpdateDependencies(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        LastFlows lastFlows,
        bool headBranchExisted,
        CancellationToken cancellationToken);

    Task MergeDependenciesAsync(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo sourceRepo,
        string targetBranch,
        string repoComparisonSha,
        string vmrComparisonSha,
        CancellationToken cancellationToken);
}

public class ForwardFlowConflictResolver : CodeFlowConflictResolver, IForwardFlowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ForwardFlowConflictResolver> _logger;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly IJsonFileMerger _jsonFileMerger;
    private readonly IVersionDetailsFileMerger _versionDetailsFileMerger;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ICommentCollector _commentCollector;

    public ForwardFlowConflictResolver(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrPatchHandler patchHandler,
        ILocalGitRepoFactory localGitRepoFactory,
        IDependencyFileManager dependencyFileManager,
        IJsonFileMerger jsonFileMerger,
        IVersionDetailsFileMerger versionDetailsFileMerger,
        IVersionDetailsParser versionDetailsParser,
        IFileSystem fileSystem,
        ICommentCollector commentCollector,
        ILogger<ForwardFlowConflictResolver> logger)
        : base(vmrInfo, patchHandler, fileSystem, commentCollector, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _fileSystem = fileSystem;
        _logger = logger;
        _localGitRepoFactory = localGitRepoFactory;
        _dependencyFileManager = dependencyFileManager;
        _jsonFileMerger = jsonFileMerger;
        _versionDetailsFileMerger = versionDetailsFileMerger;
        _versionDetailsParser = versionDetailsParser;
        _commentCollector = commentCollector;
    }

    public async Task<IReadOnlyCollection<UnixPath>> TryMergingBranchAndUpdateDependencies(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        LastFlows lastFlows,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<UnixPath> conflictedFiles = await TryMergingBranchAndResolvingConflicts(
            codeflowOptions,
            vmr,
            sourceRepo,
            lastFlows,
            headBranchExisted,
            cancellationToken);

        await DetectAndFixPartialReverts(
            codeflowOptions,
            vmr,
            sourceRepo,
            conflictedFiles,
            lastFlows,
            cancellationToken);

        try
        {
            await MergeDependenciesAsync(
                codeflowOptions,
                sourceRepo,
                codeflowOptions.HeadBranch,
                lastFlows.LastForwardFlow.RepoSha,
                // if there's a crossing flow, we need to make sure it doesn't bring in any downgrades https://github.com/dotnet/arcade-services/issues/5331
                lastFlows.CrossingFlow != null
                    ? lastFlows.LastBackFlow?.VmrSha ?? lastFlows.LastForwardFlow.VmrSha
                    : lastFlows.LastForwardFlow.VmrSha,
                cancellationToken);
        }
        catch (Exception e)
        {
            // We don't want to push this as there is some problem
            _logger.LogError(e, "Failed to update dependencies after merging {branchToMerge} into {headBranch} in {repoPath}",
                codeflowOptions.TargetBranch,
                codeflowOptions.HeadBranch,
                vmr.Path);
            throw;
        }

        return await vmr.GetConflictedFilesAsync(cancellationToken);
    }

    protected override async Task<bool> TryResolvingConflict(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        UnixPath conflictedFile,
        Codeflow? crossingFlow,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        // Known conflict in source-manifest.json
        if (string.Equals(conflictedFile, VmrInfo.DefaultRelativeSourceManifestPath, StringComparison.OrdinalIgnoreCase))
        {
            // If the repo bumped an existing submodule in this flow while the VMR reset the same submodule to a
            // different commit since the last flow (e.g. via `darc vmr reset-submodule`), the two sides genuinely
            // diverged. We must not silently overwrite the VMR's submodule state with the repo's - leave the conflict
            // so it surfaces to a human (conflict PR / darc error). See https://github.com/dotnet/arcade-services/issues/6444.
            if (await HasDivergentSubmoduleChangeAsync(vmr, codeflowOptions.Mapping.Name!, cancellationToken))
            {
                _commentCollector.AddComment(
                    $"""
                    There was a conflict in the submodule flow that needs to be resolved manually. The submodule was
                    bumped in the repository while it was independently reset in the VMR, so the correct commit for the
                    submodule cannot be determined automatically. Please choose the correct submodule commit and run
                    `darc vmr reset-submodule <sha>` to make sure the submodule ends up in the desired state in the VMR.
                    {CommentPlaceholders.NotificationTags}
                    """,
                    CommentType.Caution);
                return false;
            }

            await TryResolvingSourceManifestConflict(vmr, codeflowOptions, headBranchExisted, cancellationToken);
            return true;
        }

        var relativeRepoSourcePath = VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping);
        // If there's a conflict outside of the repo folder we're flowing
        // we don't want the changes
        if (!conflictedFile.Path.StartsWith(relativeRepoSourcePath))
        {
            await vmr.ResolveConflict(conflictedFile.Path, ours: true);
            return true;
        }

        // eng/common is always preferred from the source side
        // In rebase mode: ours=true means keep the incoming changes (source)
        // In merge mode: ours=false means prefer theirs (source being merged in)
        var engCommon = relativeRepoSourcePath / Constants.CommonScriptFilesPath;
        if (conflictedFile.Path.StartsWith(engCommon, StringComparison.InvariantCultureIgnoreCase))
        {
            await vmr.ResolveConflict(conflictedFile, ours: true);
            return true;
        }

        if (await TryDeletingFileMarkedForDeletion(vmr, conflictedFile, cancellationToken))
        {
            return true;
        }

        // Unknown conflict, but can be conflicting with a crossing flow
        // Check DetectCrossingFlow documentation for more details
        if (crossingFlow != null)
        {
            return await TryResolvingConflictWithCrossingFlow(codeflowOptions, vmr, sourceRepo, conflictedFile, crossingFlow, cancellationToken);
        }

        return false;
    }

    private async Task TryResolvingSourceManifestConflict(
        ILocalGitRepo vmr,
        CodeflowOptions codeflowOptions,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var mappingName = codeflowOptions.Mapping.Name!;
        _logger.LogDebug("Auto-resolving conflict in {file}", VmrInfo.DefaultRelativeSourceManifestPath);

        // We load the source manifest from the target branch and replace the
        // current mapping (and its submodules) with our branches' information
        var branchToShow = headBranchExisted ? codeflowOptions.HeadBranch : codeflowOptions.TargetBranch;
        var result = await vmr.RunGitCommandAsync(
            ["show", $"{branchToShow}:{VmrInfo.DefaultRelativeSourceManifestPath}"],
            cancellationToken);

        var targetBranchSourceManifest = SourceManifest.FromJson(result.StandardOutput);
        var ourSourceManifest = _sourceManifest;
        var updatedMapping = ourSourceManifest.Repositories.First(r => r.Path == mappingName);

        targetBranchSourceManifest.UpdateVersion(
            mappingName,
            updatedMapping.RemoteUri,
            updatedMapping.CommitSha,
            updatedMapping.BarId);

        var theirAffectedSubmodules = targetBranchSourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mappingName + '/'))
            .ToList();
        foreach (var submodule in theirAffectedSubmodules)
        {
            targetBranchSourceManifest.RemoveSubmodule(submodule);
        }

        var ourAffectedSubmodules = ourSourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mappingName + '/'))
            .ToList();
        foreach (var submodule in ourAffectedSubmodules)
        {
            targetBranchSourceManifest.UpdateSubmodule(submodule);
        }

        _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, targetBranchSourceManifest.ToJson());
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);
        await vmr.StageAsync([_vmrInfo.SourceManifestPath], cancellationToken);
    }

    /// <summary>
    /// Determines whether the source-manifest.json conflict is caused by a submodule that was changed on both sides:
    /// the repo bumped an existing submodule in this flow while the VMR reset the same submodule to a different commit
    /// since the last flow (e.g. via <c>darc vmr reset-submodule</c>). Such a divergence must not be auto-resolved
    /// because doing so would silently discard one side's change.
    /// The three sides are read from the in-progress merge's index stages (1 = merge base, 2 = ours, 3 = theirs).
    /// </summary>
    private static async Task<bool> HasDivergentSubmoduleChangeAsync(
        ILocalGitRepo vmr,
        string mappingName,
        CancellationToken cancellationToken)
    {
        var baseManifest = await TryReadSourceManifestStageAsync(vmr, 1, cancellationToken);
        var ourManifest = await TryReadSourceManifestStageAsync(vmr, 2, cancellationToken);
        var theirManifest = await TryReadSourceManifestStageAsync(vmr, 3, cancellationToken);

        // Without both sides of the merge we cannot reason about the change, so let the default resolution proceed.
        if (ourManifest is null || theirManifest is null)
        {
            return false;
        }

        var prefix = mappingName + '/';

        static string? GetSubmoduleSha(SourceManifest? manifest, string path)
            => manifest?.Submodules.FirstOrDefault(s => s.Path == path)?.CommitSha;

        // Look at every submodule of this mapping present at the merge base. A submodule missing from the base was
        // added by the repo in this flow (the VMR only ever resets already-tracked submodules), so it cannot diverge.
        var submodulePaths = (baseManifest?.Submodules ?? [])
            .Select(s => s.Path)
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal));

        foreach (var path in submodulePaths)
        {
            var baseSha = GetSubmoduleSha(baseManifest, path);
            var ourSha = GetSubmoduleSha(ourManifest, path);
            var theirSha = GetSubmoduleSha(theirManifest, path);

            // The submodule must exist on all three sides. A null on any side means it was added or removed, which
            // only ever originates from the repo (the VMR can only reset an already-tracked submodule), so it is not
            // a divergence. Beyond that, both the repo (ours) and the VMR (theirs) must have moved it away from the
            // base to different commits for the change to genuinely conflict.
            if (baseSha != null
                && ourSha != null
                && theirSha != null
                && ourSha != baseSha
                && theirSha != baseSha
                && ourSha != theirSha)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<SourceManifest?> TryReadSourceManifestStageAsync(
        ILocalGitRepo vmr,
        int stage,
        CancellationToken cancellationToken)
    {
        var result = await vmr.RunGitCommandAsync(
            ["show", $":{stage}:{VmrInfo.DefaultRelativeSourceManifestPath}"],
            cancellationToken);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return null;
        }

        return SourceManifest.FromJson(result.StandardOutput);
    }

    public async Task MergeDependenciesAsync(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo sourceRepo,
        string targetBranch,
        string repoComparisonSha,
        string vmrComparisonSha,
        CancellationToken cancellationToken)
    {
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        var relativeSourceMappingPath = VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name);

        await _jsonFileMerger.MergeJsonsAsync(
            vmr,
            relativeSourceMappingPath / VersionFiles.GlobalJson,
            vmrComparisonSha,
            targetBranch,
            sourceRepo,
            VersionFiles.GlobalJson,
            repoComparisonSha,
            codeflowOptions.CurrentFlow.RepoSha);

        await MergeDotNetToolsManifestIfExistsAsync(VersionFiles.DotnetToolsConfigJson);
        await MergeDotNetToolsManifestIfExistsAsync(VersionFiles.DotnetToolsJson);

        async Task MergeDotNetToolsManifestIfExistsAsync(string manifestPath)
        {
            UnixPath vmrManifestPath = relativeSourceMappingPath / manifestPath;
            bool manifestExists =
                await sourceRepo.GetFileFromGitAsync(manifestPath, repoComparisonSha) != null ||
                await sourceRepo.GetFileFromGitAsync(manifestPath, codeflowOptions.CurrentFlow.RepoSha) != null ||
                await vmr.GetFileFromGitAsync(vmrManifestPath, vmrComparisonSha) != null ||
                await vmr.GetFileFromGitAsync(vmrManifestPath, codeflowOptions.CurrentFlow.VmrSha) != null;

            if (manifestExists)
            {
                await _jsonFileMerger.MergeJsonsAsync(
                    vmr,
                    vmrManifestPath,
                    vmrComparisonSha,
                    targetBranch,
                    sourceRepo,
                    manifestPath,
                    repoComparisonSha,
                    codeflowOptions.CurrentFlow.RepoSha,
                    allowMissingFiles: true);
            }
        }

        // If Version.Details.props exists in the source repo, but not in the VMR, we create it and fill it out later.
        // This can happen if a repo was initialized inside of the vmr when it didn't have this file
        bool versionDetailsPropsCreated = false;
        if (await _dependencyFileManager.VersionDetailsPropsExistsAsync(sourceRepo.Path, branch: null!)
                && !await _dependencyFileManager.VersionDetailsPropsExistsAsync(vmr.Path, branch: null!, VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name)))
        {
            _fileSystem.WriteToFile(vmr.Path / relativeSourceMappingPath / VersionFiles.VersionDetailsProps, string.Empty);
            versionDetailsPropsCreated = true;
        }

        var versionDetailsChanges = await _versionDetailsFileMerger.MergeVersionDetails(
            vmr,
            relativeSourceMappingPath / VersionFiles.VersionDetailsXml,
            vmrComparisonSha,
            targetBranch,
            sourceRepo,
            VersionFiles.VersionDetailsXml,
            repoComparisonSha,
            codeflowOptions.CurrentFlow.RepoSha,
            codeflowOptions.Mapping.Name);

        // Also flow the Source tag if it changed
        var repoVersionDetails = await _dependencyFileManager.ParseVersionDetailsXmlAsync(
            sourceRepo.Path,
            codeflowOptions.CurrentFlow.RepoSha);
        var vmrVersionDetails = await _dependencyFileManager.ParseVersionDetailsXmlAsync(
            vmr.Path,
            targetBranch,
            relativeBasePath: relativeSourceMappingPath);

        XmlDocument? vmrVersionDetailsXml = null;
        if (repoVersionDetails.Source != null
            && repoVersionDetails.Source.BarId != vmrVersionDetails.Source?.BarId)
        {
            // Get the staged version details
            vmrVersionDetailsXml = await _dependencyFileManager.ReadVersionDetailsXmlAsync(vmr.Path, null!, relativeSourceMappingPath);
            _dependencyFileManager.UpdateVersionDetailsXmlSourceTag(vmrVersionDetailsXml, repoVersionDetails.Source);

            _fileSystem.WriteToFile(
                vmr.Path / relativeSourceMappingPath / VersionFiles.VersionDetailsXml,
                GitFile.GetIndentedXmlBody(vmrVersionDetailsXml));

            await vmr.StageAsync([relativeSourceMappingPath / VersionFiles.VersionDetailsXml], cancellationToken);
        }

        // If we didn't have any changes, and we just added Version.Details.props, we need to generate it
        if (!versionDetailsChanges.HasChanges && versionDetailsPropsCreated)
        {
            vmrVersionDetailsXml ??= await _dependencyFileManager.ReadVersionDetailsXmlAsync(vmr.Path, null!, relativeSourceMappingPath);
            var versionDetails = _versionDetailsParser.ParseVersionDetailsXml(vmrVersionDetailsXml);
            var versionPropsXml = DependencyFileManager.GenerateVersionDetailsProps(versionDetails);

            _fileSystem.WriteToFile(
                vmr.Path / relativeSourceMappingPath / VersionFiles.VersionDetailsProps,
                GitFile.GetIndentedXmlBody(versionPropsXml));

            await vmr.StageAsync([relativeSourceMappingPath / VersionFiles.VersionDetailsProps], cancellationToken);
        }

        if (!await vmr.HasWorkingTreeChangesAsync() && !await vmr.HasStagedChangesAsync())
        {
            _logger.LogInformation("No changes to dependencies in this forward flow update");
        }
    }
}
