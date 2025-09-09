// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using NuGet.Versioning;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IJsonFileMerger
{
    /// <summary>
    /// Merges the changes in a JSON file between two references in the source and target repo.
    /// </summary>
    Task MergeJsonsAsync(
        ILocalGitRepo targetRepo,
        string targetRepoJsonRelativePath,
        string targetRepoPreviousRef,
        string targetRepoCurrentRef,
        ILocalGitRepo sourceRepo,
        string sourceRepoJsonRelativePath,
        string sourceRepoPreviousRef,
        string sourceRepoCurrentRef,
        bool allowMissingFiles = false);
}

public class JsonFileMerger : VmrVersionFileMerger, IJsonFileMerger
{
    private readonly IGitRepoFactory _gitRepoFactory;
    private const string EmptyJsonString = "{}";

    public JsonFileMerger(
            IGitRepoFactory gitRepoFactory,
            ICommentCollector commentCollector)
        : base(gitRepoFactory, commentCollector)
    {
        _gitRepoFactory = gitRepoFactory;
    }

    public async Task MergeJsonsAsync(
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

        if (!allowMissingFiles || !await DeleteFileIfRequiredAsync(
                targetRepoPreviousJson,
                targetRepoCurrentJson,
                sourcePreviousJson,
                sourceCurrentJson,
                targetRepo.Path,
                targetRepoJsonRelativePath,
                targetRepoCurrentRef,
                EmptyJsonString))
        {
            var targetRepoChanges = SimpleConfigJson.Parse(targetRepoPreviousJson).GetDiff(SimpleConfigJson.Parse(targetRepoCurrentJson));
            var vmrChanges = SimpleConfigJson.Parse(sourcePreviousJson).GetDiff(SimpleConfigJson.Parse(sourceCurrentJson));

            VersionFileChanges<JsonVersionProperty> mergedChanges = MergeChanges(
                targetRepoChanges,
                vmrChanges,
                SelectNewerJsonVersionProperty,
                targetRepoJsonRelativePath,
                property => property.Replace(':', '.'));

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

    private static async Task<string> GetJsonFromGit(ILocalGitRepo repo, string jsonRelativePath, string reference, bool allowMissingFile) =>
        reference == Constants.EmptyGitObject
            ? EmptyJsonString
            : await repo.GetFileFromGitAsync(jsonRelativePath, reference)
                ?? (allowMissingFile
                    ? EmptyJsonString
                    : throw new FileNotFoundException($"File not found at {repo.Path / jsonRelativePath} for reference {reference}"));

    private static JsonVersionProperty SelectNewerJsonVersionProperty(JsonVersionProperty repoProp, JsonVersionProperty vmrProp)
    {
        if (repoProp.Value == null && vmrProp.Value == null)
        {
            throw new ArgumentException($"Compared values for '{repoProp.Name}' are null");
        }

        if (repoProp.Value == null)
        {
            return vmrProp;
        }

        if (vmrProp.Value == null)
        {
            return repoProp;
        }

        // Are the value types the same?
        if (repoProp.Value.GetType() != vmrProp.Value.GetType())
        {
            throw new ArgumentException($"Cannot compare {repoProp.Value.GetType()} with {vmrProp.Value.GetType()} because their values are of different types.");
        }

        if (repoProp.Value.Equals(vmrProp.Value))
        {
            return repoProp;
        }

        if (repoProp.Value is IEnumerable<string> repoArray && vmrProp is IEnumerable<string> vmrArray)
        {
            List<string> mergedLists = [.. repoArray.Concat(vmrArray).Distinct()];
            return new JsonVersionProperty(repoProp.Name, NodeComparisonResult.Updated, mergedLists);
        }

        if (repoProp.Value.GetType() == typeof(bool))
        {
            // if values are different, throw an exception
            if (!repoProp.Value.Equals(vmrProp.Value))
            {
                throw new ArgumentException($"Key {repoProp.Name} value has different boolean values in properties.");
            }
            return repoProp;
        }

        if (repoProp.Value.GetType() == typeof(int))
        {
            return (int)repoProp.Value > (int)vmrProp.Value ? repoProp : vmrProp;
        }

        if (repoProp.Value.GetType() == typeof(string))
        {
            // if we're able to parse both values as SemanticVersion, take the bigger one
            if (SemanticVersion.TryParse(repoProp.Value.ToString()!, out var repoVersion) &&
                SemanticVersion.TryParse(vmrProp.Value.ToString()!, out var vmrVersion))
            {
                return repoVersion > vmrVersion ? repoProp : vmrProp;
            }
            // if we can't parse both values as SemanticVersion, that means one is using a different property like $(Version), so throw an exception
            throw new ArgumentException($"Key {repoProp.Name} value has different string values in properties, and cannot be parsed as SemanticVersion");
        }

        throw new ArgumentException($"Cannot compare properties with {repoProp.Value.GetType()} values for key {repoProp.Name}.");
    }
}

