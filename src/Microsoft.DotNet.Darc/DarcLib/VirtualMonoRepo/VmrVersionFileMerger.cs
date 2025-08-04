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
        Codeflow lastFlow,
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
        Codeflow lastFlow,
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
        Codeflow lastFlow,
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

        var sourcePreviousJson = lastFlow is Backflow
            ? await GetJsonFromGit(sourceRepo, sourceRepoJsonRelativePath, sourceRepoPreviousRef, allowMissingFiles)
            : targetRepoPreviousJson;
        var sourceCurrentJson = await GetJsonFromGit(sourceRepo, sourceRepoJsonRelativePath, sourceRepoCurrentRef, allowMissingFiles);

        var targetRepoChanges = SimpleConfigJson.Parse(targetRepoPreviousJson).GetDiff(SimpleConfigJson.Parse(targetRepoCurrentJson));
        var sourceChanges = SimpleConfigJson.Parse(sourcePreviousJson).GetDiff(SimpleConfigJson.Parse(sourceCurrentJson));

        VersionFileChanges<JsonVersionProperty> mergedChanges = MergeVersionFileChanges(targetRepoChanges, sourceChanges, JsonVersionProperty.SelectJsonVersionProperty);

        var currentJson = await GetJsonFromGit(targetRepo, targetRepoJsonRelativePath, "HEAD", allowMissingFiles);
        var mergedJson = SimpleConfigJson.ApplyJsonChanges(currentJson, mergedChanges);

        var newJson = new GitFile(targetRepo.Path / targetRepoJsonRelativePath, mergedJson);
        await _localGitRepoFactory.Create(targetRepo.Path).StageAsync(["."]);

        await _gitRepoFactory.CreateClient(targetRepo.Path)
            .CommitFilesAsync(
                [newJson],
                targetRepo.Path,
                targetRepoCurrentRef,
                $"Merge {targetRepoJsonRelativePath} changes");
    }

    public async Task<VersionFileChanges<DependencyUpdate>> MergeVersionDetails(
        Codeflow lastFlow,
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
        var previousSourceRepoChanges = lastFlow is Backflow
            ? await GetDependencies(sourceRepo, sourceRepoPreviousRef, sourceRepoVersionDetailsRelativePath)
            : previousTargetRepoChanges;
        var currentSourceRepoChanges = await GetDependencies(sourceRepo, sourceRepoCurrentRef, sourceRepoVersionDetailsRelativePath);

        List<DependencyUpdate> targetChanges = ComputeChanges(
            previousTargetRepoChanges,
            currentTargetRepoChanges);

        List<DependencyUpdate> sourceChanges = ComputeChanges(
            previousSourceRepoChanges,
            currentSourceRepoChanges);

        VersionFileChanges<DependencyUpdate> mergedChanges = MergeVersionFileChanges(targetChanges, sourceChanges, SelectDependencyUpdate);

        await ApplyVersionDetailsChangesAsync(targetRepo.Path, mergedChanges, mappingToApplyChanges);

        return mergedChanges;
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
                if (addedInSource)
                {
                    throw new ConflictingDependencyUpdateException(targetChange!, sourceChange!);
                }
                // we don't have to do anything since the property is removed in the repo
                continue;
            }

            if (removedInSource)
            {
                if (addedInTarget)
                {
                    throw new ConflictingDependencyUpdateException(targetChange!, sourceChange!);
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
                additions[property] = targetChange!;
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
                updates[property] = targetChange!;
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

    private async Task ApplyVersionDetailsChangesAsync(string repoPath, VersionFileChanges<DependencyUpdate> changes, string? mapping = null)
    {
        bool versionDetailsPropsExists = await _dependencyFileManager.VersionDetailsPropsExistsAsync(repoPath, null!, mapping);
        foreach (var removal in changes.Removals)
        {
            // Remove the property from the version details
            await _dependencyFileManager.RemoveDependencyAsync(removal, repoPath, null!, mapping, versionDetailsPropsExists);
        }
        foreach ((var _, var update) in changes.Additions.Concat(changes.Updates))
        {
            await _dependencyFileManager.AddDependencyAsync(
                (DependencyDetail)update.Value!,
                repoPath,
                null!,
                mapping,
                versionDetailsPropsExists);
        }
    }

    private static async Task<string> GetJsonFromGit(ILocalGitRepo repo, string jsonRelativePath, string reference, bool allowMissingFile) =>
        await repo.GetFileFromGitAsync(jsonRelativePath, reference)
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

