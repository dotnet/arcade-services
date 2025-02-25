// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
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

public interface IVersionFileConflictResolver
{
    Task<List<DependencyUpdate>> BackflowDependenciesAndToolset(
        string mappingName,
        ILocalGitRepo targetRepo,
        string targetBranch,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        Codeflow lastFlow,
        Backflow currentFlow,
        CancellationToken cancellationToken);
}

public class VersionFileConflictResolver : IVersionFileConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ILocalLibGit2Client _libGit2Client;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IAssetLocationResolver _assetLocationResolver;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VersionFileConflictResolver> _logger;

    public VersionFileConflictResolver(
        IVmrInfo vmrInfo,
        ILocalLibGit2Client libGit2Client,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IAssetLocationResolver assetLocationResolver,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IDependencyFileManager dependencyFileManager,
        IFileSystem fileSystem,
        ILogger<VersionFileConflictResolver> logger)
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

    /// <summary>
    /// Updates version details, eng/common and other version files (global.json, ...) based on a build that is being flown.
    /// </summary>
    /// <returns>List of dependency changes</returns>
    public async Task<List<DependencyUpdate>> BackflowDependenciesAndToolset(
        string mappingName,
        ILocalGitRepo targetRepo,
        string targetBranch,
        Build build,
        IReadOnlyCollection<string>? excludedAssets,
        Codeflow lastFlow,
        Backflow currentFlow,
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
        var previousVmrDependencies = lastFlow is Backflow
            ? await GetVmrDependencies(vmr, mappingName, lastFlow.VmrSha)
            : previousRepoDependencies;
        var currentVmrDependencies = await GetVmrDependencies(vmr, mappingName, currentFlow.VmrSha);

        List<DependencyUpdate> repoChanges = ComputeChanges(
            excludedAssets,
            previousRepoDependencies,
            currentRepoDependencies);

        List<DependencyUpdate> vmrChanges = ComputeChanges(
            excludedAssets,
            previousVmrDependencies,
            currentVmrDependencies);

        List<AssetData> buildAssets = build.Assets
            .Where(a => excludedAssets is null || !excludedAssets.Contains(a.Name))
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
            .Except(excludedAssets ?? [])
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

            _logger.LogInformation("Asset {assetName} is not part of the build, not updated in the repo and not updated in the VMR. Skipping", assetName);
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
            await _dependencyFileManager.AddDependencyAsync(new DependencyDetail(update) { Version = "0.0.0" }, targetRepo.Path, branch: null!);
        }

        // If we are updating the arcade sdk we need to update the eng/common files as well
        DependencyDetail? arcadeItem = updates.GetArcadeUpdate();
        SemanticVersion? targetDotNetVersion = null;

        if (arcadeItem != null)
        {
            targetDotNetVersion = await _dependencyFileManager.ReadToolsDotnetVersionAsync(arcadeItem.RepoUri, arcadeItem.Commit, repoIsVmr: true);
        }

        GitFileContentContainer updatedFiles = await _dependencyFileManager.UpdateDependencyFiles(
            updates,
            new SourceDependency(build),
            targetRepo.Path,
            branch: null, // reads the working tree
            currentRepoDependencies.Dependencies,
            targetDotNetVersion);

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

        await targetRepo.CommitAsync(
            "Update dependencies from " + build.GetRepository(),
            allowEmpty: false,
            cancellationToken: cancellationToken);

        return
        [
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
    }

    private static List<DependencyUpdate> ComputeChanges(IReadOnlyCollection<string>? excludedAssets, VersionDetails before, VersionDetails after)
    {
        bool IsExcluded(DependencyDetail dep) => excludedAssets?.Contains(dep.Name) ?? false;

        var dependencyChanges = before.Dependencies
            .Where(dep => !IsExcluded(dep))
            .Select(dep => new DependencyUpdate()
            {
                From = dep,
            })
            .ToList();

        // Pair dependencies with the same name
        foreach (var dep in after.Dependencies.Where(dep => !IsExcluded(dep)))
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

    private async Task<VersionDetails> GetRepoDependencies(ILocalGitRepo repo, string commit)
        => GetDependencies(await repo.GetFileFromGitAsync(VersionFiles.VersionDetailsXml, commit));

    private async Task<VersionDetails> GetVmrDependencies(ILocalGitRepo vmr, string mapping, string commit)
        => GetDependencies(await vmr.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.VersionDetailsXml, commit));

    private VersionDetails GetDependencies(string? content)
        => content == null
            ? new VersionDetails([], null)
            : _versionDetailsParser.ParseVersionDetailsXml(content, includePinned: false);
}
