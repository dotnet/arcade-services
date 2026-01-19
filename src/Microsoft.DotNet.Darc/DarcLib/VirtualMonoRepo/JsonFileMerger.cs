// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
    Task<bool> MergeJsonsAsync(
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
    private readonly ICommentCollector _commentCollector;
    private readonly IFlatJsonUpdater _flatJsonUpdater;
    public const string EmptyJsonString = "{}";

    public JsonFileMerger(
            IGitRepoFactory gitRepoFactory,
            ICommentCollector commentCollector,
            IFlatJsonUpdater flatJsonUpdater)
        : base(gitRepoFactory, commentCollector)
    {
        _gitRepoFactory = gitRepoFactory;
        _commentCollector = commentCollector;
        _flatJsonUpdater = flatJsonUpdater;
    }

    public async Task<bool> MergeJsonsAsync(
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
        bool hasChanges = false;

        var targetRepoPreviousJson = await GetJsonFromGit(targetRepo, targetRepoJsonRelativePath, targetRepoPreviousRef, allowMissingFiles);
        var targetRepoCurrentJson = await GetJsonFromGit(targetRepo, targetRepoJsonRelativePath, targetRepoCurrentRef, allowMissingFiles);

        var sourcePreviousJson = await GetJsonFromGit(sourceRepo, sourceRepoJsonRelativePath, sourceRepoPreviousRef, allowMissingFiles);
        var sourceCurrentJson = await GetJsonFromGit(sourceRepo, sourceRepoJsonRelativePath, sourceRepoCurrentRef, allowMissingFiles);

        bool jsonDeletedInSource = sourcePreviousJson != EmptyJsonString
            && sourceCurrentJson == EmptyJsonString;

        bool jsonDeletedInTarget = targetRepoPreviousJson != EmptyJsonString
            && targetRepoCurrentJson == EmptyJsonString;

        if (jsonDeletedInTarget
            || (jsonDeletedInSource && targetRepoCurrentJson == EmptyJsonString))
        {
            // the target file is already deleted, nothing more to do
            return false;
        }

        if (await DeleteFileIfRequiredAsync(
                targetRepoPreviousJson,
                targetRepoCurrentJson,
                sourcePreviousJson,
                sourceCurrentJson,
                targetRepo.Path,
                targetRepoJsonRelativePath,
                targetRepoCurrentRef))
        {
            hasChanges = true;
        }
        else
        {
            var targetRepoChanges = FlatJson.Parse(targetRepoPreviousJson).GetDiff(FlatJson.Parse(targetRepoCurrentJson));
            var sourceRepoChanges = FlatJson.Parse(sourcePreviousJson).GetDiff(FlatJson.Parse(sourceCurrentJson));

            VersionFileChanges<JsonVersionProperty> mergedChanges = MergeChanges(
                targetRepoChanges,
                sourceRepoChanges,
                (repoProp, vmrProp) => SelectNewerJsonVersionProperty(targetRepoJsonRelativePath, repoProp, vmrProp),
                targetRepoJsonRelativePath,
                property => property.Replace(':', '.'));

            var currentJson = await GetJsonFromGit(targetRepo, targetRepoJsonRelativePath, "HEAD", allowMissingFiles);
            var mergedJson = _flatJsonUpdater.ApplyJsonChanges(currentJson, mergedChanges);

            hasChanges |= FlatJson.Parse(currentJson).GetDiff(FlatJson.Parse(mergedJson)).Count != 0;

            var newJson = new GitFile(targetRepo.Path / targetRepoJsonRelativePath, mergedJson);

            await _gitRepoFactory.CreateClient(targetRepo.Path)
                .CommitFilesAsync(
                    [newJson],
                    targetRepo.Path,
                    targetRepoCurrentRef,
                    $"Merge {targetRepoJsonRelativePath} changes from VMR");
        }

        await targetRepo.StageAsync([targetRepoJsonRelativePath]);

        return hasChanges;
    }

    private static async Task<string> GetJsonFromGit(ILocalGitRepo repo, string jsonRelativePath, string reference, bool allowMissingFile) =>
        reference == Constants.EmptyGitObject
            ? EmptyJsonString
            : await repo.GetFileFromGitAsync(jsonRelativePath, reference)
                ?? (allowMissingFile
                    ? EmptyJsonString
                    : throw new FileNotFoundException($"File not found at {repo.Path / jsonRelativePath} for reference {reference}"));

    private JsonVersionProperty SelectNewerJsonVersionProperty(string fileName, JsonVersionProperty repoProp, JsonVersionProperty vmrProp)
    {
        if (repoProp.Value == null && vmrProp.Value == null)
        {
            return repoProp;
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
            AddWarning(
                fileName,
                $"""
                The property {repoProp.Name} has different type in repo than in VMR:
                - Repository: `{repoProp.Value}`
                - VMR: `{vmrProp.Value}`
                """);
            return repoProp;
        }

        if (repoProp.Value.Equals(vmrProp.Value))
        {
            return repoProp;
        }

        if (repoProp.Value is IEnumerable<string> repoArray && vmrProp.Value is IEnumerable<string> vmrArray)
        {
            List<string> mergedLists = [.. repoArray.Concat(vmrArray).Distinct()];
            return new JsonVersionProperty(repoProp.Name, NodeComparisonResult.Updated, mergedLists);
        }

        if (repoProp.Value.GetType() == typeof(bool))
        {
            // If values are different, throw an exception
            if (!repoProp.Value.Equals(vmrProp.Value))
            {
                AddWarning(
                    fileName,
                    $"The property {repoProp.Name} is `{repoProp.Value}` in the repository and `{vmrProp.Value}` in the VMR.");
            }
            return repoProp;
        }

        if (repoProp.Value.GetType() == typeof(int))
        {
            return (int)repoProp.Value > (int)vmrProp.Value ? repoProp : vmrProp;
        }

        if (repoProp.Value.GetType() == typeof(string))
        {
            // If we're able to parse both values as SemanticVersion, take the bigger one
            if (SemanticVersion.TryParse(repoProp.Value.ToString()!, out var repoVersion) &&
                SemanticVersion.TryParse(vmrProp.Value.ToString()!, out var vmrVersion))
            {
                return repoVersion > vmrVersion ? repoProp : vmrProp;
            }

            AddWarning(
                fileName,
                $"""
                The property {repoProp.Name} has conflicting value:
                - Repository: `{repoProp.Value}`
                - VMR: `{vmrProp.Value}`
                """);

            return repoProp;
        }

        AddWarning(
            fileName,
            $"""
            The property {repoProp.Name} has unmergeable values:
            - Repository: `{repoProp.Value}`
            - VMR: `{vmrProp.Value}`
            """);

        return repoProp;
    }

    private void AddWarning(string fileName, string message)
    {
        _commentCollector.AddComment(
            $"""
                A conflict was detected when merging file `{fileName}`. {message}

                The repository value was used. Please verify this is the correct value or update it `{fileName}` accordingly.
                """,
            CommentType.Warning);
    }
}

