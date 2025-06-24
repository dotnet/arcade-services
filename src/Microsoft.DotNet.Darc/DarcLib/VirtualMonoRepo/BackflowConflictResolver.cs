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
using Microsoft.VisualStudio.Services.Common;
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
        SourceMapping mapping,
        CrossingFlow lastFlow,
        BackFlow currentFlow,
        CrossingFlow? crossingFlow,
        ILocalGitRepo targetRepo,
        Build build,
        string headBranch,
        string branchToMerge,
        IReadOnlyCollection<string>? excludedAssets,
        bool headBranchExisted,
        CancellationToken cancellationToken);
}

public class BackflowConflictResolver : CodeFlowConflictResolver, IBackflowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ILocalLibGit2Client _libGit2Client;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IAssetLocationResolver _assetLocationResolver;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<BackflowConflictResolver> _logger;

    public BackflowConflictResolver(
        IVmrInfo vmrInfo,
        IVmrPatchHandler patchHandler,
        ILocalLibGit2Client libGit2Client,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IAssetLocationResolver assetLocationResolver,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IDependencyFileManager dependencyFileManager,
        IFileSystem fileSystem,
        ILogger<BackflowConflictResolver> logger)
        : base(vmrInfo, patchHandler, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _libGit2Client = libGit2Client;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _assetLocationResolver = assetLocationResolver;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _dependencyFileManager = dependencyFileManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<VersionFileUpdateResult> TryMergingBranchAndUpdateDependencies(
        SourceMapping mapping,
        CrossingFlow lastFlow,
        BackFlow currentFlow,
        CrossingFlow? crossingFlow,
        ILocalGitRepo targetRepo,
        Build build,
        string headBranch,
        string branchToMerge,
        IReadOnlyCollection<string>? excludedAssets,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<UnixPath> conflictedFiles = await TryMergingBranch(
            targetRepo,
            headBranch,
            branchToMerge,
            cancellationToken);

        if (conflictedFiles.Any() && await TryResolvingConflicts(
                conflictedFiles,
                mapping,
                currentFlow,
                crossingFlow,
                targetRepo,
                headBranch,
                branchToMerge,
                headBranchExisted,
                cancellationToken))
        {
            await targetRepo.CommitAsync(
                $"""
                Merge {branchToMerge} into {headBranch}
                Auto-resolved conflicts:
                - {string.Join(Environment.NewLine + "- ", conflictedFiles.Select(f => f.Path))}
                """,
                allowEmpty: true,
                cancellationToken: CancellationToken.None);
        }

        try
        {
            var updates = await BackflowDependenciesAndToolset(
                mapping.Name,
                targetRepo,
                branchToMerge,
                build,
                excludedAssets,
                lastFlow,
                currentFlow,
                cancellationToken);
            return new VersionFileUpdateResult(conflictedFiles, updates);
        }
        catch (Exception e)
        {
            // We don't want to push this as there is some problem
            _logger.LogError(e, "Failed to update dependencies after merging {branchToMerge} into {headBranch} in {repoPath}",
                branchToMerge,
                headBranch,
                targetRepo.Path);
            throw;
        }
    }

    /// <summary>
    /// Tries to resolve well-known conflicts that can occur during a code flow operation.
    /// The conflicts can happen when backward a forward flow PRs get merged out of order
    /// and so called "crossing" flow occurs.
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
    ///       │                 └──►x 8.
    ///       │                     │
    ///
    /// In this diagram, the flows 1->5 and 3->6 are crossing each other.
    ///
    /// The conflict arises in step 8. and is caused by the fact that:
    ///   - When the forward flow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
    ///   - This means that the PR branch will be based on 1. (the real PR branch is the "actual 7.")
    ///   - This means that when 6. merged, VMR's source-manifest.json got updated with the SHA of the 3.
    ///   - So the source-manifest in 6. contains the SHA of 3.
    ///   - The forward flow PR branch contains the SHA of 5.
    ///   - So the source-manifest file conflicts on the SHA (3. vs 5.)
    ///   - There's also a similar conflict in the git-info files.
    ///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
    ///   - This is because basically we know we want to set the version files to point at 5.
    /// </summary>
    private async Task<bool> TryResolvingConflicts(
        IReadOnlyCollection<UnixPath> conflictedFiles,
        SourceMapping mapping,
        CrossingFlow currentFlow,
        CrossingFlow? crossingFlow,
        ILocalGitRepo repo,
        string headBranch,
        string branchToMerge,
        bool headBranchExisted,
        CancellationToken cancellationToken)
    {
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        foreach (var conflictedFile in conflictedFiles)
        {
            // Known version file - check out the branch version, we want to override it
            // See https://github.com/dotnet/arcade-services/issues/4865
            if (DependencyFileManager.DependencyFiles.Any(f => f.Equals(conflictedFile, StringComparison.OrdinalIgnoreCase)))
            {
                // Revert files so that we can resolve the conflicts
                // We use the target branch version when we are flowing the first time (because we did not flow the version files yet)
                // We use the head branch version when we are flowing again because it already has updates from previous flow
                // plus it can contain additional changes from the PR
                await repo.RunGitCommandAsync(["checkout", headBranchExisted ? "--ours" : "--theirs", conflictedFile], cancellationToken);
                continue;
            }

            // Unknown conflict, but can be conflicting with a out-of-order recent flow
            // Check DetectCrossingFlow documentation for more details
            if (crossingFlow != null)
            {
                if (await TryResolvingConflictWithCrossingFlow(
                    mapping.Name,
                    vmr,
                    repo,
                    conflictedFile,
                    currentFlow,
                    crossingFlow,
                    cancellationToken))
                {
                    continue;
                }
            }

            _logger.LogInformation("Failed to merge the branch {branchToMerge} into {headBranch} due to unresolvable conflict in {conflictedFile}",
                branchToMerge,
                headBranch,
                conflictedFile);

            await AbortMerge(repo);
            return false;
        }

        _logger.LogInformation("Successfully auto-resolved all conflicts between {branchToMerge} and {headBranch}",
            branchToMerge,
            headBranch);

        return true;
    }

    /// <summary>
    /// Updates version details, eng/common and other version files (global.json, ...) based on a build that is being flown.
    /// </summary>
    /// <returns>List of dependency changes</returns>
    private async Task<List<DependencyUpdate>> BackflowDependenciesAndToolset(
        string mappingName,
        ILocalGitRepo targetRepo,
        string targetBranch,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        CrossingFlow lastFlow,
        BackFlow currentFlow,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Resolving backflow dependency updates between VMR {vmrSha1}..{vmrSha2} and {repo} {repoSha1}..{repoSha2}",
            lastFlow.VmrSha,
            Constants.HEAD,
            mappingName,
            lastFlow.RepoSha,
            targetBranch);

        var previousRepoDependencies = await GetRepoDependencies(targetRepo, lastFlow.RepoSha);
        var currentRepoDependencies = await GetRepoDependencies(targetRepo, targetBranch);

        // Similarly to the code flow algorithm, we compare the corresponding commits
        // and the contents of the version files inside.
        // We distinguish the direction of the previous flow vs the current flow.
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        var previousVmrDependencies = lastFlow is BackFlow
            ? await GetVmrDependencies(vmr, mappingName, lastFlow.VmrSha)
            : previousRepoDependencies;
        var currentVmrDependencies = await GetVmrDependencies(vmr, mappingName, currentFlow.VmrSha);

        var excludedAssetsMatcher = excludedAssets.GetAssetMatcher();

        List<DependencyUpdate> repoChanges = ComputeChanges(
            excludedAssetsMatcher,
            previousRepoDependencies,
            currentRepoDependencies);

        List<DependencyUpdate> vmrChanges = ComputeChanges(
            excludedAssetsMatcher,
            previousVmrDependencies,
            currentVmrDependencies);

        List<AssetData> buildAssets = build.Assets
            .Where(a => !excludedAssetsMatcher.IsExcluded(a.Name))
            .Select(a => new AssetData(a.NonShipping)
            {
                Name = a.Name,
                Version = a.Version
            })
            .ToList();

        // All packages that can appear in current dependency update
        var headBranchDependencies = await GetRepoDependencies(targetRepo, commit: null! /* working tree */);
        var uniquePackages = headBranchDependencies.Dependencies
            .Select(dep => dep.Name)
            .Concat(vmrChanges.Select(c => c.From?.Name ?? c.To.Name))
            .Concat(repoChanges.Select(c => c.From?.Name ?? c.To.Name))
            .Distinct()
            .Where(dep => !excludedAssetsMatcher.IsExcluded(dep))
            .ToHashSet();

        var versionUpdates = new List<DependencyDetail>();
        var buildUpdates = new List<AssetData>();
        var removals = new HashSet<string>();
        var additions = new List<DependencyDetail>();

        foreach (var assetName in uniquePackages)
        {
            AssetData? buildAsset = buildAssets.FirstOrDefault(a => a.Name == assetName);
            DependencyUpdate? repoChange = repoChanges.FirstOrDefault(c => assetName == (c.From?.Name ?? c.To!.Name));
            DependencyUpdate? vmrChange = vmrChanges.FirstOrDefault(c => assetName == (c.From?.Name ?? c.To!.Name));

            bool repoAddition = repoChange != null && repoChange.From == null;
            bool vmrAddition = vmrChange != null && vmrChange.From == null;
            bool repoRemoval = repoChange != null && repoChange.To == null;
            bool vmrRemoval = vmrChange != null && vmrChange.To == null;
            bool includedInBuild = buildAsset != null;
            bool repoUpdated = repoChange?.From != null && repoChange.To != null;
            bool vmrUpdated = vmrChange?.From != null && vmrChange.To != null;

            DependencyDetail? repoVersion = repoChange?.To;
            DependencyDetail? vmrVersion = vmrChange?.To;

            // When part of the build, we use the version from the build
            // This is the most common case
            if (includedInBuild)
            {
                _logger.LogInformation("Asset {assetName} contained in build, updating to {version}", assetName, buildAsset!.Version);

                buildUpdates.Add(new AssetData(false)
                {
                    Name = assetName,
                    Version = buildAsset.Version,
                });

                continue;
            }

            if (repoUpdated && vmrUpdated)
            {
                if (SemanticVersion.TryParse(repoVersion!.Version, out var repoSemVer) && SemanticVersion.TryParse(vmrVersion!.Version, out var vmrSemVer))
                {
                    DependencyDetail newerVersion = repoSemVer > vmrSemVer ? repoVersion! : vmrVersion!;
                    _logger.LogInformation(
                        "Asset {assetName} updated to {repoVersion} in the repo and {vmrVersion} in the VMR. Choosing the newer version {newerVersion}",
                        assetName,
                        repoVersion.Version,
                        vmrVersion.Version,
                        newerVersion.Version);
                    versionUpdates.Add(newerVersion);
                    continue;
                }

                // We can't tell which one is newer, we pick the repo version
                _logger.LogInformation(
                    "Asset {assetName} updated to {repoVersion} in the repo and {vmrVersion} in the VMR. Choosing the repo version {repoVersion}",
                    assetName,
                    repoVersion.Version,
                    vmrVersion!.Version,
                    repoVersion.Version);
                versionUpdates.Add(repoVersion);
                continue;
            }

            if (repoRemoval)
            {
                if (vmrAddition)
                {
                    // Asset was removed from the repo and added to the VMR at the same time
                    _logger.LogInformation(
                        "Asset {assetName} was removed from the repo and added to the VMR at the same time. Skipping the asset",
                        assetName);
                    throw new ConflictingDependencyUpdateException(repoChange!, vmrChange!);
                }

                // Asset got removed from the repo, we can skip it (it won't be in the target V.D.xml)
                _logger.LogInformation("Asset {assetName} was removed from the repo", assetName);
                continue;
            }

            if (vmrRemoval)
            {
                if (repoAddition)
                {
                    // Asset was removed from the VMR and added to the repo at the same time
                    _logger.LogInformation(
                        "Asset {assetName} was removed from the VMR and added to the repo at the same time. Skipping the asset",
                        assetName);
                    throw new ConflictingDependencyUpdateException(repoChange!, vmrChange!);
                }

                // Asset got removed from the VMR, we need to remove it
                removals.Add(assetName);
                _logger.LogInformation("Asset {assetName} was removed from the VMR, removing from repo too", assetName);
                continue;
            }

            if (repoAddition || vmrAddition)
            {
                if (repoAddition && vmrAddition && SemanticVersion.TryParse(repoVersion!.Version, out var repoSemVer) && SemanticVersion.TryParse(vmrVersion!.Version, out var vmrSemVer))
                {
                    DependencyDetail newerVersion = repoSemVer > vmrSemVer ? repoVersion! : vmrVersion!;
                    _logger.LogInformation(
                        "Asset {assetName} added in both the repo ({repoVersion}) and the VMR ({vmrVersion}). Choosing the newer version {newerVersion}",
                        assetName,
                        repoVersion.Version,
                        vmrVersion.Version,
                        newerVersion.Version);
                    additions.Add(newerVersion);
                    continue;
                }

                if (repoAddition)
                {
                    // Asset was added to the repo, no change necessary
                    _logger.LogInformation("Asset {assetName} was added in the repo", assetName);
                    additions.Add(repoVersion!);
                    continue;
                }

                if (vmrAddition)
                {
                    // Asset was added to the VMR, we need to add it
                    _logger.LogInformation("Asset {assetName} version {version} was added in the VMR, adding it to the repo too", assetName, vmrVersion!.Version);
                    additions.Add(vmrVersion);
                    continue;
                }
            }

            if (repoUpdated)
            {
                // Don't do anything, package is updated in the repo already
                _logger.LogInformation("Asset {assetName} updated to {version} in the repo, skipping", assetName, repoVersion!.Version);
                continue;
            }

            if (vmrUpdated)
            {
                // Package got updated in the VMR only
                _logger.LogInformation("Asset {assetName} updated to {version} in the VMR, updating in the repo too", assetName, vmrVersion!.Version);
                versionUpdates.Add(vmrVersion);
                continue;
            }

            _logger.LogDebug("Asset {assetName} is not part of the build, not updated in the repo and not updated in the VMR. Skipping", assetName);
        }

        foreach (var removedAsset in removals)
        {
            await _dependencyFileManager.RemoveDependencyAsync(removedAsset, targetRepo.Path, null!);
        }

        foreach (var addedDependency in additions)
        {
            await _dependencyFileManager.AddDependencyAsync(addedDependency, targetRepo.Path, branch: null!);
        }

        currentRepoDependencies = await GetRepoDependencies(targetRepo, null!);

        List<DependencyDetail> updates = _coherencyUpdateResolver
            .GetRequiredNonCoherencyUpdates(
                build.GetRepository(),
                build.Commit,
                buildUpdates,
                currentRepoDependencies.Dependencies)
            .Select(u => u.To)
            .Concat(versionUpdates)
            .ToList();

        await _assetLocationResolver.AddAssetLocationToDependenciesAsync(updates);

        // We add all of the packages since it's a harmless operation just to be sure they made through all the merging
        // Later we update them
        foreach (var update in updates)
        {
            await _dependencyFileManager.AddDependencyAsync(new DependencyDetail(update), targetRepo.Path, branch: null!);
        }

        // If we are updating the arcade sdk we need to update the eng/common files as well
        DependencyDetail? arcadeItem = updates.GetArcadeUpdate();

        SemanticVersion? targetDotNetVersion = null;

        // The arcade backflow subscriptions has all assets excluded, but we want to update the global.json sdk version anyway
        if (arcadeItem != null || mappingName == "arcade")
        {
            // Even tho we are backflowing from the VMR, we want to get the sdk version from VMR`s global.json, not src/arcade's
            targetDotNetVersion = await _dependencyFileManager.ReadToolsDotnetVersionAsync(build.GetRepository(), build.Commit, repoIsVmr: false);
        }

        GitFileContentContainer updatedFiles = await _dependencyFileManager.UpdateDependencyFiles(
            updates,
            new SourceDependency(build, mappingName),
            targetRepo.Path,
            branch: null, // reads the working tree
            currentRepoDependencies.Dependencies,
            targetDotNetVersion,
            // make sure that we always backflow the VMRs root global.json sdk version
            forceGlobalJsonUpdate: true);

        // This actually does not commit but stages only
        await _libGit2Client.CommitFilesAsync(updatedFiles.GetFilesToCommit(), targetRepo.Path, null, null);

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
            }
        }

        if (!await targetRepo.HasWorkingTreeChangesAsync())
        {
            _logger.LogInformation("No changes to dependencies in this backflow update");
            return [];
        }

        await targetRepo.StageAsync(["."], cancellationToken);

        List<DependencyUpdate> dependencyUpdates = [
            ..additions
                .Where(addition => headBranchDependencies.Dependencies.All(a => addition.Name != a.Name))
                .Select(addition => new DependencyUpdate()
                {
                    From = null,
                    To = addition
                }),
            ..removals
                .Where(addition => headBranchDependencies.Dependencies.Any(a => addition == a.Name))
                .Select(removal => new DependencyUpdate()
                {
                    From = headBranchDependencies.Dependencies.First(d => d.Name == removal),
                    To = null
                }),
            ..updates
                .Select(update => new DependencyUpdate()
                {
                    From = headBranchDependencies.Dependencies.Concat(additions).First(d => d.Name == update.Name),
                    To = update,
                }),
        ];

        string commitMessage = string.Concat(
            $"Update dependencies from {build.GetRepository()} build {build.Id}",
            Environment.NewLine,
            BuildDependencyUpdateCommitMessage(dependencyUpdates));

        await targetRepo.CommitAsync(
            commitMessage,
            allowEmpty: false,
            cancellationToken: cancellationToken);

        return dependencyUpdates;
    }

    private static List<DependencyUpdate> ComputeChanges(IAssetMatcher excludedAssetsMatcher, VersionDetails before, VersionDetails after)
    {
        var dependencyChanges = before.Dependencies
            .Where(dep => !excludedAssetsMatcher.IsExcluded(dep.Name))
            .Select(dep => new DependencyUpdate()
            {
                From = dep,
            })
            .ToList();

        // Pair dependencies with the same name
        foreach (var dep in after.Dependencies.Where(dep => !excludedAssetsMatcher.IsExcluded(dep.Name)))
        {
            var existing = dependencyChanges.FirstOrDefault(d => d.From?.Name == dep.Name);
            if (existing != null)
            {
                existing.To = dep;
            }
            else
            {
                dependencyChanges.Add(new DependencyUpdate()
                {
                    From = null,
                    To = dep,
                });
            }
        }

        // Check if there are any actual changes
        return dependencyChanges
            .Where(change => change.From?.Version != change.To?.Version
                || change.From?.Commit != change.To?.Commit
                || change.From?.RepoUri != change.To?.RepoUri)
            .ToList();
    }

    public static string BuildDependencyUpdateCommitMessage(IEnumerable<DependencyUpdate> updates)
    {
        if (!updates.Any())
        {
            return "No dependency updates to commit";
        }
        Dictionary<string, List<string>> removedDependencies = new();
        Dictionary<string, List<string>> addedDependencies = new();
        Dictionary<string, List<string>> updatedDependencies = new();
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
        if (updatedDependencies.Any())
        {
            result.AppendLine("Updated Dependencies:");
            foreach ((string versionBlurb, List<string> packageNames) in updatedDependencies)
            {
                result.AppendLine(string.Join(", ", packageNames) + $" ({versionBlurb})");
            }
            result.AppendLine();
        }
        if (addedDependencies.Any())
        {
            result.AppendLine("Added Dependencies:");
            foreach ((string versionBlurb, List<string> packageNames) in addedDependencies)
            {
                result.AppendLine(string.Join(", ", packageNames) + $" ({versionBlurb})");
            }
            result.AppendLine();
        }
        if (removedDependencies.Any())
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
            dictionary[versionBlurb] = new List<string> { dependencyName };
        }
    }

    private async Task<VersionDetails> GetRepoDependencies(ILocalGitRepo repo, string commit)
        => GetDependencies(await repo.GetFileFromGitAsync(VersionFiles.VersionDetailsXml, commit));

    private async Task<VersionDetails> GetVmrDependencies(ILocalGitRepo vmr, string mapping, string commit)
        => GetDependencies(await vmr.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.VersionDetailsXml, commit));

    private VersionDetails GetDependencies(string? content)
        => content == null
            ? new VersionDetails([], null)
            : _versionDetailsParser.ParseVersionDetailsXml(content, includePinned: false);
}
