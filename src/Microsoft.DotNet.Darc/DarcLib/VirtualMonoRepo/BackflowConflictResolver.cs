// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IBackflowConflictResolver
{
    /// <summary>
    /// Tries to merge the target branch into the head branch and then updates the dependencies and toolset.
    /// Calculates the dependency updates based on the current build assets, the changes in the VMR and the repo.
    /// </summary>
    /// <returns>List of dependency updates made to the version files</returns>
    Task<VersionFileUpdateResult> TryMergingBranchAndUpdateDependencies(
        CodeflowOptions codeflowOptions,
        LastFlows lastFlows,
        ILocalGitRepo targetRepo,
        bool headBranchExisted,
        CancellationToken cancellationToken);
}

public class BackflowConflictResolver : CodeFlowConflictResolver, IBackflowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly ILocalLibGit2Client _libGit2Client;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IAssetLocationResolver _assetLocationResolver;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly IFileSystem _fileSystem;
    private readonly IJsonFileMerger _jsonFileMerger;
    private readonly IVersionDetailsFileMerger _versionDetailsFileMerger;
    private readonly ILogger<BackflowConflictResolver> _logger;

    public BackflowConflictResolver(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrPatchHandler patchHandler,
        ILocalLibGit2Client libGit2Client,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IAssetLocationResolver assetLocationResolver,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IDependencyFileManager dependencyFileManager,
        IJsonFileMerger jsonFileMerger,
        IVersionDetailsFileMerger versionDetailsFileMerger,
        IFileSystem fileSystem,
        ILogger<BackflowConflictResolver> logger)
        : base(vmrInfo, patchHandler, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _libGit2Client = libGit2Client;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _assetLocationResolver = assetLocationResolver;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _dependencyFileManager = dependencyFileManager;
        _jsonFileMerger = jsonFileMerger;
        _versionDetailsFileMerger = versionDetailsFileMerger;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<VersionFileUpdateResult> TryMergingBranchAndUpdateDependencies(
        CodeflowOptions codeflowOptions,
        LastFlows lastFlows,
        ILocalGitRepo targetRepo,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        // If we are rebasing, we are already on top of the branch and we don't need to merge it
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        IReadOnlyCollection<UnixPath> conflictedFiles = await TryMergingBranchAndResolvingConflicts(
            codeflowOptions,
            vmr,
            targetRepo,
            lastFlows,
            headBranchExisted,
            cancellationToken);

        await DetectAndFixPartialReverts(
            codeflowOptions,
            vmr,
            targetRepo,
            conflictedFiles,
            lastFlows,
            cancellationToken);

        try
        {
            string repoComparisonSha, vmrComparisonSha;
            if (headBranchExisted)
            {
                repoComparisonSha = lastFlows.LastFlow.RepoSha;
                vmrComparisonSha = lastFlows.LastFlow.VmrSha;
            }
            // if there's a crossing flow, we need to make sure it doesn't bring in any downgrades https://github.com/dotnet/arcade-services/issues/5331
            else if (lastFlows.CrossingFlow != null)
            {
                repoComparisonSha = lastFlows.LastForwardFlow.RepoSha;
                vmrComparisonSha = lastFlows.LastBackFlow?.VmrSha ?? lastFlows.LastForwardFlow.VmrSha;
            }
            else if (lastFlows.LastBackFlow != null)
            {
                repoComparisonSha = lastFlows.LastBackFlow.RepoSha;
                vmrComparisonSha = lastFlows.LastBackFlow.VmrSha;
            }
            else
            {
                // If there were no backflows, this means we only had forward flows.
                // We need to make sure that we capture all changes made in the forward flows by comparing the current dependencies against an empty commit
                repoComparisonSha = Constants.EmptyGitObject;
                vmrComparisonSha = Constants.EmptyGitObject;
            }

            var hasToolsetUpdates = await BackflowToolsets(
                codeflowOptions,
                targetRepo,
                codeflowOptions.TargetBranch,
                repoComparisonSha,
                vmrComparisonSha);

            var updates = await BackflowDependencies(
                codeflowOptions,
                targetRepo,
                codeflowOptions.TargetBranch,
                repoComparisonSha,
                vmrComparisonSha,
                cancellationToken);

            return new VersionFileUpdateResult(conflictedFiles, updates, hasToolsetUpdates);
        }
        catch (Exception e)
        {
            // We don't want to push this as there is some problem
            _logger.LogError(e, "Failed to update dependencies after merging {branchToMerge} into {headBranch} in {repoPath}",
                codeflowOptions.TargetBranch,
                codeflowOptions.HeadBranch,
                targetRepo.Path);
            throw;
        }
    }

    protected override async Task<bool> TryResolvingConflict(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo vmr,
        ILocalGitRepo targetRepo,
        UnixPath conflictedFile,
        Codeflow? crossingFlow,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        // Known version file - check out the branch version, we want to override it
        // See https://github.com/dotnet/arcade-services/issues/4865
        if (DependencyFileManager.CodeflowDependencyFiles
            .Concat(VersionFiles.NugetConfigNames) // TODO https://github.com/dotnet/arcade-services/issues/5897, 
            .Any(f => f.Equals(conflictedFile, StringComparison.OrdinalIgnoreCase)))
        {
            // Revert files so that we can resolve the conflicts
            // We use the target branch version when we are flowing the first time (because we did not flow the version files yet)
            // We use the head branch version when we are flowing again because it already has updates from previous flow
            // plus it can contain additional changes from the PR
            await targetRepo.ResolveConflict(conflictedFile, ours: headBranchExisted);
            return true;
        }

        // eng/common is always preferred from the source side
        // In rebase mode: ours=true means keep the incoming changes (source)
        // In merge mode: ours=false means prefer theirs (source being merged in)
        if (conflictedFile.Path.StartsWith(Constants.CommonScriptFilesPath, StringComparison.InvariantCultureIgnoreCase))
        {
            await targetRepo.ResolveConflict(conflictedFile, ours: true);
            return true;
        }

        if (await TryDeletingFileMarkedForDeletion(targetRepo, conflictedFile, cancellationToken))
        {
            return true;
        }

        // Unknown conflict, but can be conflicting with a crossing flow
        // Check DetectCrossingFlow documentation for more details
        if (crossingFlow != null)
        {
            return await TryResolvingConflictWithCrossingFlow(codeflowOptions, vmr, targetRepo, conflictedFile, crossingFlow, cancellationToken);
        }

        return false;
    }

    private async Task<bool> BackflowToolsets(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo targetRepo,
        string targetBranch,
        string repoComparisonSha,
        string vmrComparisonSha)
    {
        bool hasUpdates = false;
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        // handle global.json
        hasUpdates |= await _jsonFileMerger.MergeJsonsAsync(
            targetRepo,
            VersionFiles.GlobalJson,
            repoComparisonSha,
            targetBranch,
            vmr,
            VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name) / VersionFiles.GlobalJson,
            vmrComparisonSha,
            codeflowOptions.CurrentFlow.VmrSha);

        // and handle dotnet-tools.json if it exists
        bool dotnetToolsConfigExists =
            (await targetRepo.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, repoComparisonSha) != null) ||
            (await vmr.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name) / VersionFiles.DotnetToolsConfigJson, codeflowOptions.CurrentFlow.VmrSha) != null ||
            (await targetRepo.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, targetBranch) != null) ||
            (await vmr.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name) / VersionFiles.DotnetToolsConfigJson, vmrComparisonSha) != null));

        if (dotnetToolsConfigExists)
        {
            hasUpdates |= await _jsonFileMerger.MergeJsonsAsync(
                    targetRepo,
                    VersionFiles.DotnetToolsConfigJson,
                    repoComparisonSha,
                    targetBranch,
                    vmr,
                    VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name) / VersionFiles.DotnetToolsConfigJson,
                    vmrComparisonSha,
                    codeflowOptions.CurrentFlow.VmrSha,
                    allowMissingFiles: true);
        }
        return hasUpdates;
    }

    /// <summary>
    /// Updates version details, eng/common and other version files (global.json, ...) based on a build that is being flown.
    /// </summary>
    /// <returns>List of dependency changes</returns>
    private async Task<List<DependencyUpdate>> BackflowDependencies(
        CodeflowOptions codeflowOptions,
        ILocalGitRepo targetRepo,
        string targetBranch,
        string repoComparisonSha,
        string vmrComparisonSha,
        CancellationToken cancellationToken)
    {
        var headBranchDependencies = await GetRepoDependencies(targetRepo, commit: null /* working tree */);
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);


        var versionDetailsChanges = await _versionDetailsFileMerger.MergeVersionDetails(
            targetRepo,
            VersionFiles.VersionDetailsXml,
            repoComparisonSha,
            targetBranch,
            vmr,
            VmrInfo.GetRelativeRepoSourcesPath(codeflowOptions.Mapping.Name) / VersionFiles.VersionDetailsXml,
            vmrComparisonSha,
            codeflowOptions.CurrentFlow.VmrSha,
            // we're applying the changes to a product repo, so no mapping
            mappingToApplyChanges: null);

        var excludedAssetsMatcher = new NameBasedAssetMatcher(codeflowOptions.ExcludedAssets);
        List<AssetData> buildAssets = codeflowOptions.Build.Assets
            .Where(a => !excludedAssetsMatcher.IsExcluded(a.Name))
            .Select(a => new AssetData(a.NonShipping)
            {
                Name = a.Name,
                Version = a.Version
            })
            .ToList();

        var currentRepoDependencies = await GetRepoDependencies(targetRepo, commit: null /* working tree */);

        List<DependencyDetail> buildUpdates = _coherencyUpdateResolver
            .GetRequiredNonCoherencyUpdates(
                codeflowOptions.Build.GetRepository(),
                codeflowOptions.Build.Commit,
                buildAssets,
                currentRepoDependencies.Dependencies)
            .Select(u => u.To)
            .ToList();

        await _assetLocationResolver.AddAssetLocationToDependenciesAsync(buildUpdates);

        // If we are updating the arcade sdk we need to update the eng/common files as well
        DependencyDetail? arcadeItem = buildUpdates.GetArcadeUpdate();

        SemanticVersion? targetDotNetVersion = null;

        // The arcade backflow subscriptions has all assets excluded, but we want to update the global.json sdk version anyway
        if (arcadeItem != null || codeflowOptions.Mapping.Name == VmrInfo.ArcadeMappingName)
        {
            // Even tho we are backflowing from the VMR, we want to get the sdk version from VMR`s global.json, not src/arcade's
            targetDotNetVersion = await _dependencyFileManager.ReadToolsDotnetVersionAsync(
                codeflowOptions.Build.GetRepository(),
                codeflowOptions.Build.Commit);
        }

        GitFileContentContainer updatedFiles = await _dependencyFileManager.UpdateDependencyFiles(
            buildUpdates,
            new SourceDependency(codeflowOptions.Build, codeflowOptions.Mapping.Name),
            targetRepo.Path,
            branch: null, // reads the working tree
            currentRepoDependencies.Dependencies,
            targetDotNetVersion);

        // This actually does not commit but stages only
        var filesToCommit = updatedFiles.GetFilesToCommit();
        await _libGit2Client.CommitFilesAsync(filesToCommit, targetRepo.Path, branch: null, commitMessage: null);

        // Update eng/common files
        if (arcadeItem != null)
        {
            // Check if the VMR contains src/arcade/eng/common
            var arcadeEngCommonDir = _vmrInfo.VmrPath / VmrInfo.ArcadeRepoDir / Constants.CommonScriptFilesPath;
            if (!_fileSystem.DirectoryExists(arcadeEngCommonDir))
            {
                _logger.LogWarning($"VMR does not contain {VmrInfo.ArcadeRepoDir / Constants.CommonScriptFilesPath}. Skipping the common scripts update");
            }
            else
            {
                _logger.LogInformation("Updating eng/common files from the VMR's {path}", arcadeEngCommonDir);

                var commonDir = targetRepo.Path / Constants.CommonScriptFilesPath;
                if (_fileSystem.DirectoryExists(commonDir))
                {
                    _fileSystem.DeleteDirectory(commonDir, true);
                }

                _fileSystem.CopyDirectory(
                    arcadeEngCommonDir,
                    targetRepo.Path / Constants.CommonScriptFilesPath,
                    true);

                await targetRepo.StageAsync([Constants.CommonScriptFilesPath], cancellationToken);
            }
        }

        if (!await targetRepo.HasWorkingTreeChangesAsync() && !await targetRepo.HasStagedChangesAsync())
        {
            _logger.LogInformation("No changes to dependencies in this backflow update");
            return [];
        }

        await targetRepo.StageAsync([..filesToCommit.Select(f => f.FilePath)], cancellationToken);

        Dictionary<string, DependencyDetail> allUpdates = buildUpdates.ToDictionary(u => u.Name, comparer: StringComparer.OrdinalIgnoreCase);

        // if a repo was added during the merge and then updated, it's not an update, but an addition
        foreach ((var key, var addition) in versionDetailsChanges.Additions)
        {
            if (allUpdates.TryGetValue(key, out var updatedDependencyDetail))
            {
                var depDetail = (DependencyDetail)addition.Value!;
                depDetail.Version = updatedDependencyDetail.Version;
                depDetail.Commit = updatedDependencyDetail.Commit;
                depDetail.Pinned = updatedDependencyDetail.Pinned;
                
                allUpdates.Remove(key);
            }
        }

        // Add updates tha are not a part of the build updates
        foreach ((var _, var update) in versionDetailsChanges.Updates)
        {
            var updateDetail = (DependencyDetail)update.Value!;
            if (!allUpdates.ContainsKey(updateDetail.Name))
            {
                allUpdates[updateDetail.Name] = updateDetail;
            }
        }

        var headBranchDependencyDict = headBranchDependencies.Dependencies.ToDictionary(d => d.Name, d => d, comparer: StringComparer.OrdinalIgnoreCase);

        List<DependencyUpdate> dependencyUpdates = [
            ..versionDetailsChanges.Additions
                .Select(addition => new DependencyUpdate()
                {
                    From = null,
                    To = (DependencyDetail)addition.Value.Value!
                }),
            ..versionDetailsChanges.Removals
                .Select(removal => new DependencyUpdate()
                {
                    From = headBranchDependencyDict[removal],
                    To = null
                }),
            ..allUpdates
                .Select(update => new DependencyUpdate()
                {
                    From = headBranchDependencyDict.TryGetValue(update.Key, out DependencyDetail? value)
                        ? value : (DependencyDetail)versionDetailsChanges.Additions[update.Key].Value!,
                    To = update.Value,
                })
                .Where(update =>
                    update.From.Version != update.To.Version
                    || update.From.RepoUri != update.To.RepoUri
                    || update.From.Commit != update.To.Commit),
        ];

        return dependencyUpdates;
    }

    public static string BuildDependencyUpdateCommitMessage(IEnumerable<DependencyUpdate> updates)
    {
        if (!updates.Any())
        {
            return "No dependency updates to commit";
        }
        Dictionary<string, List<string>> removedDependencies = [];
        Dictionary<string, List<string>> addedDependencies = [];
        Dictionary<string, List<string>> updatedDependencies = [];
        foreach (DependencyUpdate dependencyUpdate in updates)
        {
            if (dependencyUpdate.To != null && dependencyUpdate.From == null)
            {
                string versionBlurb = $"Version {dependencyUpdate.To.Version}";
                AddDependencyToDictionary(addedDependencies, versionBlurb, dependencyUpdate.To.Name);
                continue;
            }
            if (dependencyUpdate.To == null && dependencyUpdate.From != null)
            {
                string versionBlurb = $"Version {dependencyUpdate.From.Version}";
                AddDependencyToDictionary(removedDependencies, versionBlurb, dependencyUpdate.From.Name);
                continue;
            }
            if (dependencyUpdate.To != null && dependencyUpdate.From != null)
            {
                string versionBlurb = $"Version {dependencyUpdate.From.Version} -> {dependencyUpdate.To.Version}";
                AddDependencyToDictionary(updatedDependencies, versionBlurb, dependencyUpdate.From.Name);
                continue;
            }
        }

        var result = new StringBuilder();
        if (updatedDependencies.Count != 0)
        {
            result.AppendLine("Updated Dependencies:");
            foreach ((string versionBlurb, List<string> packageNames) in updatedDependencies)
            {
                result.AppendLine(string.Join(", ", packageNames) + $" ({versionBlurb})");
            }
            result.AppendLine();
        }
        if (addedDependencies.Count != 0)
        {
            result.AppendLine("Added Dependencies:");
            foreach ((string versionBlurb, List<string> packageNames) in addedDependencies)
            {
                result.AppendLine(string.Join(", ", packageNames) + $" ({versionBlurb})");
            }
            result.AppendLine();
        }
        if (removedDependencies.Count != 0)
        {
            result.AppendLine("Removed Dependencies:");
            foreach ((string versionBlurb, List<string> packageNames) in removedDependencies)
            {
                result.AppendLine(string.Join(", ", packageNames) + $" ({versionBlurb})");
            }
            result.AppendLine();
        }
        return result.ToString().TrimEnd();
    }

    private static void AddDependencyToDictionary(Dictionary<string, List<string>> dictionary, string versionBlurb, string dependencyName)
    {
        if (dictionary.TryGetValue(versionBlurb, out var list))
        {
            list.Add(dependencyName);
        }
        else
        {
            dictionary[versionBlurb] = [dependencyName];
        }
    }

    private async Task<VersionDetails> GetRepoDependencies(ILocalGitRepo repo, string? commit)
        => GetDependencies(await repo.GetFileFromGitAsync(VersionFiles.VersionDetailsXml, commit));

    private VersionDetails GetDependencies(string? content)
        => content == null
            ? new VersionDetails([], null)
            : _versionDetailsParser.ParseVersionDetailsXml(content, includePinned: false);

    protected override IEnumerable<string> GetPatchExclusions(SourceMapping mapping) =>
    [
        .. base.GetPatchExclusions(mapping),
        .. VmrBackFlower.GetPatchExclusions(_sourceManifest, mapping)
    ];
}
