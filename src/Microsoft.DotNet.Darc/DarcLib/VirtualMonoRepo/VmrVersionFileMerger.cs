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
using VersionFileChanges = (System.Collections.Generic.List<string> removals,
        System.Collections.Generic.List<Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo.VersionFileProperty> additions,
        System.Collections.Generic.List<Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo.VersionFileProperty> updates);

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
        string jsonRelativePath);

    Task MergeVersionDetails(
        Codeflow lastFlow,
        Codeflow currentFlow,
        string mappingName,
        ILocalGitRepo targetRepo,
        string targetBranch,
        IAssetMatcher excludedAssetsMatcher);
}

public class VmrVersionFileMerger : IVmrVersionFileMerger
{
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ILogger<VmrVersionFileMerger> _logger;
    private readonly IVmrInfo _vmrInfo;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IDependencyFileManager _dependencyFileManager;

    public VmrVersionFileMerger(IGitRepoFactory gitRepoFactory,
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
        string jsonRelativePath)
    {
        var targetRepoPreviousJson = await targetRepo.GetFileFromGitAsync(jsonRelativePath, targetRepoPreviousRef)
            ?? throw new FileNotFoundException($"File not found at {targetRepo.Path / jsonRelativePath} for reference {targetRepoPreviousRef}");
        var targetRepoCurrentJson = await targetRepo.GetFileFromGitAsync(jsonRelativePath, targetRepoCurrentRef)
            ?? throw new FileNotFoundException($"File not found at {targetRepo.Path / jsonRelativePath} for reference {targetRepoCurrentRef}");

        var vmrPreviousJson = lastFlow is Backflow 
            ? await vmr.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mappingName) / jsonRelativePath, vmrPreviousRef)
                ?? throw new FileNotFoundException($"File not found at {vmr.Path / (VmrInfo.GetRelativeRepoSourcesPath(mappingName) / jsonRelativePath)} for reference {vmrPreviousRef}")
            : targetRepoPreviousJson;
        var vmrCurrentJson = await vmr.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mappingName) / jsonRelativePath, vmrCurrentRef)
            ?? throw new FileNotFoundException($"File not found at {vmr.Path / (VmrInfo.GetRelativeRepoSourcesPath(mappingName) / jsonRelativePath)} for reference {vmrCurrentRef}");

        var targetRepoChanges = FlatJsonComparer.CompareFlatJsons(
            JsonFlattener.FlattenJsonToDictionary(targetRepoPreviousJson),
            JsonFlattener.FlattenJsonToDictionary(targetRepoCurrentJson));
        var vmrChanges = FlatJsonComparer.CompareFlatJsons(
            JsonFlattener.FlattenJsonToDictionary(vmrPreviousJson),
            JsonFlattener.FlattenJsonToDictionary(vmrCurrentJson));

        var finalChanges = MergeDependencyChanges(targetRepoChanges, vmrChanges);

        var mergedJson = ApplyJsonChanges(targetRepoCurrentJson, finalChanges);

        var newJson = new GitFile(jsonRelativePath, mergedJson);
        await _gitRepoFactory.CreateClient(targetRepo.Path)
            .CommitFilesAsync(
                [newJson],
                targetRepo.Path,
                targetRepoCurrentRef,
                $"Merge {jsonRelativePath} changes from VMR");
    }

    public async Task MergeVersionDetails(
        Codeflow lastFlow,
        Codeflow currentFlow,
        string mappingName,
        ILocalGitRepo targetRepo,
        string targetBranch,
        IAssetMatcher excludedAssetsMatcher)
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
            excludedAssetsMatcher,
            previousRepoDependencies,
            currentRepoDependencies);

        List<DependencyUpdate> vmrChanges = ComputeChanges(
            excludedAssetsMatcher,
            previousVmrDependencies,
            currentVmrDependencies);

        var mergedChanges = MergeDependencyChanges(repoChanges, vmrChanges);

        await ApplyVersionDetailsChangesAsync(targetRepo.Path, mergedChanges);
    }

    private VersionFileChanges MergeDependencyChanges(
        IReadOnlyCollection<VersionFileProperty> repoChanges,
        IReadOnlyCollection<VersionFileProperty> vmrChanges)
    {
        var changedProperties = repoChanges
            .Concat(vmrChanges)
            .Select(c => c.Name);

        var removals = new List<string>();
        var additions = new List<VersionFileProperty>();
        var updates = new List<VersionFileProperty>();
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
                    throw new ArgumentException($"Key {property} is added in one repo and removed in the other json, which is not allowed.");
                }
                // we don't have to do anything since the property is removed in the repo
                continue;
            }

            if (removedInVmr)
            {
                if (addedInRepo)
                {
                    throw new ArgumentException($"Key {property} is added in one repo and removed in the other json, which is not allowed.");
                }
                removals.Add(property);
                continue;
            }

            if (addedInRepo && addedInVmr)
            {
                additions.Add(repoChange! > vmrChange! ? vmrChange! : repoChange!);
                continue;
            }
            if (addedInRepo)
            {
                // we don't have to do anything since the property is added in the repo
                continue;
            }
            if (addedInVmr)
            {
                additions.Add(vmrChange!);
                continue;
            }

            if (updatedInRepo && updatedInVmr)
            {
                updates.Add(repoChange! > vmrChange! ? vmrChange! : repoChange!);
                continue;
            }
            if (updatedInRepo)
            {
                // we don't have to do anything since the property is updated in the repo
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
}

