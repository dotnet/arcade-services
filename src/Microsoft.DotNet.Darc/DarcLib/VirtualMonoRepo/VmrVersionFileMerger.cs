// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrVersionFileMerger
{
    /// <summary>
    /// Merges the changes in a JSON file between two references in the source and target repo.
    /// </summary>
    Task MergeJsonAsync(
        ILocalGitRepo targetRepo,
        string targetRepoJsonRelativePath,
        string targetRepoPreviousRef,
        string targetRepoCurrentRef,
        ILocalGitRepo sourceRepo,
        string sourceRepoJsonRelativePath,
        string sourceRepoPreviousRef,
        string sourceRepoCurrentRef,
        bool allowMissingFiles = false);

    Task<VersionFileChanges<DependencyUpdate>> MergeVersionDetails(
        ILocalGitRepo targetRepo,
        string targetRepoVersionDetailsRelativePath,
        string targetRepoPreviousRef,
        string targetRepoCurrentRef,
        ILocalGitRepo sourceRepo,
        string sourceRepoVersionDetailsRelativePath,
        string sourceRepoPreviousRef,
        string sourceRepoCurrentRef,
        string? mappingToApplyChanges);
}

public class VmrVersionFileMerger : IVmrVersionFileMerger
{
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ILogger<VmrVersionFileMerger> _logger;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IDependencyFileManager _dependencyFileManager;
    private const string EmptyJsonString = "{}";

    public VmrVersionFileMerger(
        IGitRepoFactory gitRepoFactory,
        ILogger<VmrVersionFileMerger> logger,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IDependencyFileManager dependencyFileManager)
    {
        _gitRepoFactory = gitRepoFactory;
        _logger = logger;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _dependencyFileManager = dependencyFileManager;
    }

    public async Task MergeJsonAsync(
        ILocalGitRepo targetRepo,
        string targetRepoJsonRelativePath,
        string targetRepoPreviousRef,
        string targetRepoCurrentRef,
        ILocalGitRepo sourceRepo,
        string sourceRepoJsonRelativePath,
        string sourceRepoPreviousRef,
        string sourceRepoCurrentRef,
        bool allowMissingFiles = false)
    {
        var targetRepoPreviousJson = await GetJsonFromGit(targetRepo, targetRepoJsonRelativePath, targetRepoPreviousRef, allowMissingFiles);
        var targetRepoCurrentJson = await GetJsonFromGit(targetRepo, targetRepoJsonRelativePath, targetRepoCurrentRef, allowMissingFiles);

        var sourcePreviousJson = await GetJsonFromGit(sourceRepo, sourceRepoJsonRelativePath, sourceRepoPreviousRef, allowMissingFiles);
        var sourceCurrentJson = await GetJsonFromGit(sourceRepo, sourceRepoJsonRelativePath, sourceRepoCurrentRef, allowMissingFiles);

        if (!allowMissingFiles || !(await DeleteFileIfRequiredAsync(
                targetRepoPreviousJson,
                targetRepoCurrentJson,
                sourcePreviousJson,
                sourceCurrentJson,
                targetRepo.Path,
                targetRepoJsonRelativePath,
                targetRepoCurrentRef,
                EmptyJsonString)))
        {
            var targetRepoChanges = SimpleConfigJson.Parse(targetRepoPreviousJson).GetDiff(SimpleConfigJson.Parse(targetRepoCurrentJson));
            var vmrChanges = SimpleConfigJson.Parse(sourcePreviousJson).GetDiff(SimpleConfigJson.Parse(sourceCurrentJson));

            VersionFileChanges<JsonVersionProperty> mergedChanges = MergeVersionFileChanges(targetRepoChanges, vmrChanges, JsonVersionProperty.SelectJsonVersionProperty);

            var currentJson = await GetJsonFromGit(targetRepo, targetRepoJsonRelativePath, "HEAD", allowMissingFiles);
            var mergedJson = SimpleConfigJson.ApplyJsonChanges(currentJson, mergedChanges);

            var newJson = new GitFile(targetRepo.Path / targetRepoJsonRelativePath, mergedJson);

            await _gitRepoFactory.CreateClient(targetRepo.Path)
                .CommitFilesAsync(
                    [newJson],
                    targetRepo.Path,
                    targetRepoCurrentRef,
                    $"Merge {targetRepoJsonRelativePath} changes from VMR");
        }
      
        await targetRepo.StageAsync(["."]);
    }

    public async Task<VersionFileChanges<DependencyUpdate>> MergeVersionDetails(
        ILocalGitRepo targetRepo,
        string targetRepoVersionDetailsRelativePath,
        string targetRepoPreviousRef,
        string targetRepoCurrentRef,
        ILocalGitRepo sourceRepo,
        string sourceRepoVersionDetailsRelativePath,
        string sourceRepoPreviousRef,
        string sourceRepoCurrentRef,
        string? mappingToApplyChanges)
    {
        _logger.LogInformation(
            "Resolving dependency updates between {sourceRepo} {sourceSha1}..{sourceSha2} and {targetRepo} {targetSha1}..{targetSha2}",
            sourceRepo.Path,
            sourceRepoPreviousRef,
            Constants.HEAD,
            targetRepo.Path,
            targetRepoPreviousRef,
            targetRepoCurrentRef);

        var previousTargetRepoChanges = await GetDependencies(targetRepo, targetRepoPreviousRef, targetRepoVersionDetailsRelativePath);
        var currentTargetRepoChanges = await GetDependencies(targetRepo, targetRepoCurrentRef, targetRepoVersionDetailsRelativePath);

        // Similarly to the code flow algorithm, we compare the corresponding commits
        // and the contents of the version files inside.
        // We distinguish the direction of the previous flow vs the current flow.
        var previousSourceRepoChanges = await GetDependencies(sourceRepo, sourceRepoPreviousRef, sourceRepoVersionDetailsRelativePath);
        var currentSourceRepoChanges = await GetDependencies(sourceRepo, sourceRepoCurrentRef, sourceRepoVersionDetailsRelativePath);

        List<DependencyUpdate> targetChanges = ComputeChanges(
            previousTargetRepoChanges,
            currentTargetRepoChanges);

        List<DependencyUpdate> sourceChanges = ComputeChanges(
            previousSourceRepoChanges,
            currentSourceRepoChanges);

        VersionFileChanges<DependencyUpdate> mergedChanges = MergeVersionFileChanges(targetChanges, sourceChanges, SelectDependencyUpdate);

        return await ApplyVersionDetailsChangesAsync(targetRepo, mergedChanges, mappingToApplyChanges);
    }

    private async Task<bool> DeleteFileIfRequiredAsync(
        string targetRepoPreviousJson,
        string targetRepoCurrentJson,
        string sourceRepoPreviousJson,
        string sourceRepoCurrentJson,
        NativePath repoPath,
        string filePath,
        string targetRepoCurrentRef,
        string emptyContent)
    {
        // was it deleted in the target repo?
        if (targetRepoPreviousJson != emptyContent && targetRepoCurrentJson == emptyContent)
        {
            // no need to do anything, it's already deleted
            return true;
        }
        // was it deleted in the source repo?
        if (sourceRepoPreviousJson != emptyContent && sourceRepoCurrentJson == emptyContent)
        {
            var deletedJson = new GitFile(repoPath / filePath, targetRepoCurrentJson, ContentEncoding.Utf8, operation: GitFileOperation.Delete);
            await _gitRepoFactory.CreateClient(repoPath)
                .CommitFilesAsync(
                    [deletedJson],
                    repoPath,
                    targetRepoCurrentRef,
                    $"Delete {filePath} in target repo");
            return true;
        }

        return false;
    }

    private VersionFileChanges<T> MergeVersionFileChanges<T>(
        IReadOnlyCollection<T> targetChanges,
        IReadOnlyCollection<T> sourceChanges,
        Func<T, T, T> selector) where T : IVersionFileProperty
    {
        var changedProperties = targetChanges
            .Concat(sourceChanges)
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var removals = new List<string>();
        var additions = new Dictionary<string, T>();
        var updates = new Dictionary<string, T>();
        
        foreach (var property in changedProperties)
        {
            var targetChange = targetChanges.FirstOrDefault(c => c.Name == property);
            var sourceChange = sourceChanges.FirstOrDefault(c => c.Name == property);

            var addedInTarget = targetChange != null && targetChange.IsAdded();
            var addedInSource = sourceChange != null && sourceChange.IsAdded();
            var removedInTarget = targetChange != null && targetChange.IsRemoved();
            var removedInSource = sourceChange != null && sourceChange.IsRemoved();
            var updateInTarget = targetChange != null && targetChange.IsUpdated();
            var updatedInSource = sourceChange != null && sourceChange.IsUpdated();

            if (removedInTarget)
            {
                // we don't have to do anything since the property is removed in the repo
                // even if the property was add in the source repo, we'll take what's in the target repo
                // TODO https://github.com/dotnet/arcade-services/issues/5176 check if the source repo is adding a dependency here and write a comment about the conflict
                continue;
            }

            if (removedInSource)
            {
                if (addedInTarget)
                {
                    // even if the property was removed in the source repo, we'll take whatever is in the target repo
                    // TODO https://github.com/dotnet/arcade-services/issues/5176 check if the source repo is adding a dependency here and write a comment about the conflict
                    continue;
                }
                removals.Add(property);
                continue;
            }

            if (addedInTarget && addedInSource)
            {
                additions[property] = selector(targetChange!, sourceChange!);
                continue;
            }
            if (addedInTarget)
            {
                // the property is already in the targe repo, so we don't need to add it again
                continue;
            }
            if (addedInSource)
            {
                additions[property] = sourceChange!;
                continue;
            }

            if (updateInTarget && updatedInSource)
            {
                updates[property] = selector(targetChange!, sourceChange!);
                continue;
            }
            if (updateInTarget)
            {
                // the property is already updated in the target repo, so we don't need to update it again
                continue;
            }
            if (updatedInSource)
            {
                updates[property] = sourceChange!;
                continue;
            }
        }

        return new VersionFileChanges<T>(removals, additions, updates);
    }

    private async Task<VersionDetails> GetDependencies(ILocalGitRepo repo, string commit, string relativePath)
    {
        var content = await repo.GetFileFromGitAsync(relativePath, commit);
        return content == null
            ? new VersionDetails([], null)
            : _versionDetailsParser.ParseVersionDetailsXml(content, includePinned: false);
    }

    private static List<DependencyUpdate> ComputeChanges(VersionDetails before, VersionDetails after)
    {
        var dependencyChanges = before.Dependencies
            .Select(dep => new DependencyUpdate()
            {
                From = dep,
            })
            .ToList();

        // Pair dependencies with the same name
        foreach (var dep in after.Dependencies)
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

    private async Task<VersionFileChanges<DependencyUpdate>> ApplyVersionDetailsChangesAsync(ILocalGitRepo repo, VersionFileChanges<DependencyUpdate> changes, string? mapping = null)
    {
        var versionFilesBasePath = mapping != null
            ? VmrInfo.GetRelativeRepoSourcesPath(mapping)
            : null;
        bool versionDetailsPropsExists = await _dependencyFileManager.VersionDetailsPropsExistsAsync(repo.Path, null!, versionFilesBasePath);
        VersionFileChanges<DependencyUpdate> appliedChanges = new([], [], []);
        foreach (var removal in changes.Removals)
        {
            // Remove the property from the version details
            if (await _dependencyFileManager.TryRemoveDependencyAsync(removal, repo.Path, null!, versionFilesBasePath, versionDetailsPropsExists))
            {
                appliedChanges.Removals.Add(removal);
            }
        }
        foreach ((var key, var addition) in changes.Additions)
        {
            if (await _dependencyFileManager.TryAddOrUpdateDependency(
                (DependencyDetail)addition.Value!,
                repo.Path,
                null!,
                versionFilesBasePath,
                versionDetailsOnly: true,
                versionDetailsPropsExists))
            {
                appliedChanges.Additions[key] = addition;
            }
        }
        foreach ((var key, var update) in changes.Updates)
        {
            if (await _dependencyFileManager.TryAddOrUpdateDependency(
                (DependencyDetail)update.Value!,
                repo.Path,
                null!,
                versionFilesBasePath,
                versionDetailsOnly: true,
                versionDetailsPropsExists))
            {
                appliedChanges.Updates[key] = update;
            }
        }

        await repo.StageAsync(["."]);

        return appliedChanges;
    }

    private static async Task<string> GetJsonFromGit(ILocalGitRepo repo, string jsonRelativePath, string reference, bool allowMissingFile) =>
        reference == Constants.EmptyGitObject
            ? EmptyJsonString
            : await repo.GetFileFromGitAsync(jsonRelativePath, reference)
                ?? (allowMissingFile
                    ? EmptyJsonString
                    : throw new FileNotFoundException($"File not found at {repo.Path / jsonRelativePath} for reference {reference}"));

    private static DependencyUpdate SelectDependencyUpdate(DependencyUpdate repo1Change, DependencyUpdate repo2Change)
    {
        if (SemanticVersion.TryParse(repo1Change.To?.Version!, out var repo1Version) &&
            SemanticVersion.TryParse(repo2Change.To?.Version!, out var repo2Version))
        {
            return repo1Version > repo2Version ? repo1Change : repo2Change;
        }
        
        throw new ArgumentException($"Cannot compare {repo1Change.To?.Version} with {repo2Change.To?.Version} because they are not valid semantic versions.");
    }
}

