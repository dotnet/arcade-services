// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
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
    /// Merges the changes in a JSON file between two references in the target repo and the VMR.
    /// </summary>
    Task MergeJsonAsync(
        Codeflow lastFlow,
        ILocalGitRepo targetRepo,
        string targetRepoPreviousRef,
        string targetRepoCurrentRef,
        ILocalGitRepo vmr,
        string vmrPreviousRef,
        string vmrCurrentRef,
        string mapping,
        string jsonRelativePath,
        bool allowMissingFiles = false);

    Task<VersionFileChanges<DependencyUpdate>> MergeVersionDetails(
        Codeflow lastFlow,
        Codeflow currentFlow,
        string mappingName,
        ILocalGitRepo targetRepo,
        string targetBranch);
}

public class VmrVersionFileMerger : IVmrVersionFileMerger
{
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ILogger<VmrVersionFileMerger> _logger;
    private readonly IVmrInfo _vmrInfo;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IDependencyFileManager _dependencyFileManager;
    private const string EmptyJsonString = "{}";

    public VmrVersionFileMerger(
        IGitRepoFactory gitRepoFactory,
        ILogger<VmrVersionFileMerger> logger,
        IVmrInfo vmrInfo,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IDependencyFileManager dependencyFileManager)
    {
        _gitRepoFactory = gitRepoFactory;
        _logger = logger;
        _vmrInfo = vmrInfo;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _dependencyFileManager = dependencyFileManager;
    }

    public async Task MergeJsonAsync(
        Codeflow lastFlow,
        ILocalGitRepo targetRepo,
        string targetRepoPreviousRef,
        string targetRepoCurrentRef,
        ILocalGitRepo vmr,
        string vmrPreviousRef,
        string vmrCurrentRef,
        string mappingName,
        string jsonRelativePath,
        bool allowMissingFiles = false)
    {
        var targetRepoPreviousJson = await GetJsonFromGit(targetRepo, jsonRelativePath, targetRepoPreviousRef, allowMissingFiles);
        var targetRepoCurrentJson = await GetJsonFromGit(targetRepo, jsonRelativePath, targetRepoCurrentRef, allowMissingFiles);

        var vmrJsonPath = VmrInfo.GetRelativeRepoSourcesPath(mappingName) / jsonRelativePath;
        var vmrPreviousJson = lastFlow is Backflow
            ? await GetJsonFromGit(vmr, vmrJsonPath, vmrPreviousRef, allowMissingFiles)
            : targetRepoPreviousJson;
        var vmrCurrentJson = await GetJsonFromGit(vmr, vmrJsonPath, vmrCurrentRef, allowMissingFiles);

        var targetRepoChanges = SimpleConfigJson.Parse(targetRepoPreviousJson).GetDiff(SimpleConfigJson.Parse(targetRepoCurrentJson));
        var vmrChanges = SimpleConfigJson.Parse(vmrPreviousJson).GetDiff(SimpleConfigJson.Parse(vmrCurrentJson));

        VersionFileChanges<JsonVersionProperty> mergedChanges = MergeVersionFileChanges(targetRepoChanges, vmrChanges, JsonVersionProperty.SelectJsonVersionProperty);

        var currentJson = await GetJsonFromGit(targetRepo, jsonRelativePath, "HEAD", allowMissingFiles);
        var mergedJson = SimpleConfigJson.ApplyJsonChanges(currentJson, mergedChanges);

        var newJson = new GitFile(targetRepo.Path / jsonRelativePath, mergedJson);
        await _localGitRepoFactory.Create(targetRepo.Path).StageAsync(["."]);

        await _gitRepoFactory.CreateClient(targetRepo.Path)
            .CommitFilesAsync(
                [newJson],
                targetRepo.Path,
                targetRepoCurrentRef,
                $"Merge {jsonRelativePath} changes from VMR");
    }

    public async Task<VersionFileChanges<DependencyUpdate>> MergeVersionDetails(
        Codeflow lastFlow,
        Codeflow currentFlow,
        string mappingName,
        ILocalGitRepo targetRepo,
        string targetBranch)
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
            previousRepoDependencies,
            currentRepoDependencies);

        List<DependencyUpdate> vmrChanges = ComputeChanges(
            previousVmrDependencies,
            currentVmrDependencies);

        VersionFileChanges<DependencyUpdate> mergedChanges = MergeVersionFileChanges(repoChanges, vmrChanges, SelectDependencyUpdate);

        await ApplyVersionDetailsChangesAsync(targetRepo.Path, mergedChanges);

        return mergedChanges;
    }

    private VersionFileChanges<T> MergeVersionFileChanges<T>(
        IReadOnlyCollection<T> repoChanges,
        IReadOnlyCollection<T> vmrChanges,
        Func<T, T, T> selector) where T : IVersionFileProperty
    {
        var changedProperties = repoChanges
            .Concat(vmrChanges)
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var removals = new List<string>();
        var additions = new Dictionary<string, T>();
        var updates = new Dictionary<string, T>();
        
        foreach (var property in changedProperties)
        {
            var repoChange = repoChanges.FirstOrDefault(c => c.Name == property);
            var vmrChange = vmrChanges.FirstOrDefault(c => c.Name == property);

            var addedInRepo = repoChange != null && repoChange.IsAdded();
            var addedInVmr = vmrChange != null && vmrChange.IsAdded();
            var removedInRepo = repoChange != null && repoChange.IsRemoved();
            var removedInVmr = vmrChange != null && vmrChange.IsRemoved();
            var updatedInRepo = repoChange != null && repoChange.IsUpdated();
            var updatedInVmr = vmrChange != null && vmrChange.IsUpdated();

            if (removedInRepo)
            {
                if (addedInVmr)
                {
                    throw new ConflictingDependencyUpdateException(repoChange!, vmrChange!);
                }
                // we don't have to do anything since the property is removed in the repo
                continue;
            }

            if (removedInVmr)
            {
                if (addedInRepo)
                {
                    throw new ConflictingDependencyUpdateException(repoChange!, vmrChange!);
                }
                removals.Add(property);
                continue;
            }

            if (addedInRepo && addedInVmr)
            {
                additions[property] = selector(repoChange!, vmrChange!);
                continue;
            }
            if (addedInRepo)
            {
                additions[property] = repoChange!;
                continue;
            }
            if (addedInVmr)
            {
                additions[property] = vmrChange!;
                continue;
            }

            if (updatedInRepo && updatedInVmr)
            {
                updates[property] = selector(repoChange!, vmrChange!);
                continue;
            }
            if (updatedInRepo)
            {
                updates[property] = repoChange!;
                continue;
            }
            if (updatedInVmr)
            {
                updates[property] = vmrChange!;
                continue;
            }
        }

        return new VersionFileChanges<T>(removals, additions, updates);
    }

    private async Task<VersionDetails> GetRepoDependencies(ILocalGitRepo repo, string commit)
        => GetDependencies(await repo.GetFileFromGitAsync(VersionFiles.VersionDetailsXml, commit));

    private async Task<VersionDetails> GetVmrDependencies(ILocalGitRepo vmr, string mapping, string commit)
        => GetDependencies(await vmr.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.VersionDetailsXml, commit));

    private VersionDetails GetDependencies(string? content)
        => content == null
            ? new VersionDetails([], null)
            : _versionDetailsParser.ParseVersionDetailsXml(content, includePinned: false);

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

    private async Task ApplyVersionDetailsChangesAsync(string repoPath, VersionFileChanges<DependencyUpdate> changes)
    {
        foreach (var removal in changes.Removals)
        {
            // Remove the property from the version details
            await _dependencyFileManager.RemoveDependencyAsync(removal, repoPath, null!);
        }
        foreach ((var _, var update) in changes.Additions.Concat(changes.Updates))
        {
            await _dependencyFileManager.AddDependencyAsync(
                (DependencyDetail)update.Value!,
                repoPath,
                null!);
        }
    }

    private static async Task<string> GetJsonFromGit(ILocalGitRepo repo, string jsonRelativePath, string reference, bool allowMissingFile) =>
        await repo.GetFileFromGitAsync(jsonRelativePath, reference)
            ?? (allowMissingFile
                ? EmptyJsonString
                : throw new FileNotFoundException($"File not found at {repo.Path / jsonRelativePath} for reference {reference}"));

    private static DependencyUpdate SelectDependencyUpdate(DependencyUpdate repoChange, DependencyUpdate vmrChange)
    {
        if (SemanticVersion.TryParse(repoChange.To?.Version!, out var repoVersion) &&
            SemanticVersion.TryParse(vmrChange.To?.Version!, out var vmrVersion))
        {
            return repoVersion > vmrVersion ? repoChange : vmrChange;
        }
        
        throw new ArgumentException($"Cannot compare {repoChange.To?.Version} with {vmrChange.To?.Version} because they are not valid semantic versions.");
    }
}

