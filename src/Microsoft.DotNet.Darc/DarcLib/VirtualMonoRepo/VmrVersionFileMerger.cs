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
using VersionFileChanges = (System.Collections.Generic.List<string> removals,
        System.Collections.Generic.List<Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo.IVersionFileProperty> additions,
        System.Collections.Generic.List<Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo.IVersionFileProperty> updates);

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

    Task<VersionFileChanges> MergeVersionDetails(
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

        var targetRepoChanges = FlatJsonComparer.CompareFlatJsons(
            SimpleConfigJsonFlattener.FlattenSimpleConfigJsonToDictionary(targetRepoPreviousJson),
            SimpleConfigJsonFlattener.FlattenSimpleConfigJsonToDictionary(targetRepoCurrentJson));
        var vmrChanges = FlatJsonComparer.CompareFlatJsons(
            SimpleConfigJsonFlattener.FlattenSimpleConfigJsonToDictionary(vmrPreviousJson),
            SimpleConfigJsonFlattener.FlattenSimpleConfigJsonToDictionary(vmrCurrentJson));

        var finalChanges = MergeDependencyChanges(targetRepoChanges, vmrChanges, JsonVersionProperty.SelectJsonProperty);

        var currentJson = await GetJsonFromGit(targetRepo, jsonRelativePath, "HEAD", allowMissingFiles);
        var mergedJson = ApplyJsonChanges(currentJson, finalChanges);

        var newJson = new GitFile(targetRepo.Path / jsonRelativePath, mergedJson);
        await _localGitRepoFactory.Create(targetRepo.Path).StageAsync(["."]);

        await _gitRepoFactory.CreateClient(targetRepo.Path)
            .CommitFilesAsync(
                [newJson],
                targetRepo.Path,
                targetRepoCurrentRef,
                $"Merge {jsonRelativePath} changes from VMR");
    }

    public async Task<VersionFileChanges> MergeVersionDetails(
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

        var mergedChanges = MergeDependencyChanges(repoChanges, vmrChanges, VersionDetailsSelector);

        await ApplyVersionDetailsChangesAsync(targetRepo.Path, mergedChanges);

        return mergedChanges;
    }

    private VersionFileChanges MergeDependencyChanges(
        IReadOnlyCollection<IVersionFileProperty> repoChanges,
        IReadOnlyCollection<IVersionFileProperty> vmrChanges,
        Func<IVersionFileProperty, IVersionFileProperty, IVersionFileProperty> select)
    {
        var changedProperties = repoChanges
            .Concat(vmrChanges)
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var removals = new List<string>();
        var additions = new List<IVersionFileProperty>();
        var updates = new List<IVersionFileProperty>();
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
                additions.Add(select(repoChange!, vmrChange!));
                continue;
            }
            if (addedInRepo)
            {
                additions.Add(repoChange!);
                continue;
            }
            if (addedInVmr)
            {
                additions.Add(vmrChange!);
                continue;
            }

            if (updatedInRepo && updatedInVmr)
            {
                updates.Add(select(repoChange!, vmrChange!));
                continue;
            }
            if (updatedInRepo)
            {
                updates.Add(repoChange!);
                continue;
            }
            if (updatedInVmr)
            {
                updates.Add(vmrChange!);
                continue;
            }
        }

        return (removals, additions, updates);
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

    private string ApplyJsonChanges(
        string file,
        VersionFileChanges changes)
    {
        JsonNode rootNode = JsonNode.Parse(file) ?? throw new InvalidOperationException("Failed to parse JSON file.");

        foreach (string removal in changes.removals)
        {
            RemoveJsonProperty(rootNode, removal);
        }
        foreach (var change in changes.additions.Concat(changes.updates))
        {
            AddOrUpdateJsonProperty(rootNode, (JsonVersionProperty)change);
        }

        return rootNode.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        });
    }

    private static void RemoveJsonProperty(JsonNode root, string path)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);

        JsonNode currentNode = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (currentNode is not JsonObject obj || !obj.ContainsKey(segments[i]))
            {
                throw new InvalidOperationException($"Cannot navigate to {segments[i]} in JSON structure.");
            }

            currentNode = obj[segments[i]]!;
        }

        // Remove the property from its parent
        if (currentNode is JsonObject parentObject)
        {
            string propertyToRemove = segments[segments.Length - 1];
            parentObject.Remove(propertyToRemove);
        }
    }

    private static void AddOrUpdateJsonProperty(JsonNode root, JsonVersionProperty property)
    {
        var segments = property.Name.Split(':', StringSplitOptions.RemoveEmptyEntries);
        JsonNode currentNode = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (currentNode is not JsonObject obj)
            {
                throw new InvalidOperationException($"Cannot navigate to {segments[i]} in JSON structure.");
            }
            if (!obj.ContainsKey(segments[i]))
            {
                obj[segments[i]] = new JsonObject();
            }
            currentNode = obj[segments[i]]!;
        }
        // Add the property to its parent
        if (currentNode is JsonObject parentObject)
        {
            string propertyToAdd = segments[segments.Length - 1];
            parentObject[propertyToAdd] = JsonValue.Create(property.Value);
        }
    }

    private async Task ApplyVersionDetailsChangesAsync(string repoPath, VersionFileChanges changes)
    {
        foreach (var removal in changes.removals)
        {
            // Remove the property from the version details
            await _dependencyFileManager.RemoveDependencyAsync(removal, repoPath, null!);
        }
        foreach (var update in changes.additions.Concat(changes.updates))
        {
            await _dependencyFileManager.AddDependencyAsync(
                (DependencyDetail)update.Value!,
                repoPath,
                null!);
        }
    }

    public IVersionFileProperty VersionDetailsSelector(IVersionFileProperty repoChange, IVersionFileProperty vmrChange)
    {
        if (repoChange.GetType() != typeof(DependencyUpdate) || vmrChange.GetType() != typeof(DependencyUpdate))
        {
            throw new ArgumentException($"Provided updates are not {typeof(DependencyUpdate)}");
        }
        var repoUpdate = (DependencyUpdate)repoChange;
        var vmrUpdate = (DependencyUpdate)vmrChange;

        if (SemanticVersion.TryParse(repoUpdate.To?.Version!, out var repoVersion) &&
            SemanticVersion.TryParse(vmrUpdate.To?.Version!, out var vmrVersion))
        {
            return repoVersion > vmrVersion ? repoChange : vmrChange;
        }
        throw new ArgumentException($"Cannot compare {repoUpdate.To?.Version} with {vmrUpdate.To?.Version} because they are not valid semantic versions.");
    }

    private static async Task<string> GetJsonFromGit(ILocalGitRepo repo, string jsonRelativePath, string reference, bool allowMissingFile) =>
        await repo.GetFileFromGitAsync(jsonRelativePath, reference)
            ?? (allowMissingFile
                ? EmptyJsonString
                : throw new FileNotFoundException($"File not found at {repo.Path / jsonRelativePath} for reference {reference}"));
}

