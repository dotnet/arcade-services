// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public abstract class VmrVersionFileMerger
{
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ICommentCollector _commentCollector;

    protected VmrVersionFileMerger(
        IGitRepoFactory gitRepoFactory,
        ICommentCollector commentCollector)
    {
        _gitRepoFactory = gitRepoFactory;
        _commentCollector = commentCollector;
    }

    protected async Task<bool> DeleteFileIfRequiredAsync(
        string targetRepoPreviousJson,
        string targetRepoCurrentJson,
        string sourceRepoPreviousJson,
        string sourceRepoCurrentJson,
        NativePath repoPath,
        string filePath,
        string targetRepoCurrentRef)
    {
        if (sourceRepoPreviousJson != JsonFileMerger.EmptyJsonString
            && sourceRepoCurrentJson == JsonFileMerger.EmptyJsonString
            && targetRepoCurrentJson != JsonFileMerger.EmptyJsonString)
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

    protected VersionFileChanges<T> MergeChanges<T>(
        IReadOnlyCollection<T> targetChanges,
        IReadOnlyCollection<T> sourceChanges,
        Func<T, T, T> selector,
        string fileTargetRepoPath,
        Func<string, string> propertyNameTransformer) where T : IVersionFileProperty
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

            var addedInTarget = targetChange != null && targetChange.IsAdded;
            var addedInSource = sourceChange != null && sourceChange.IsAdded;
            var removedInTarget = targetChange != null && targetChange.IsRemoved;
            var removedInSource = sourceChange != null && sourceChange.IsRemoved;
            var updateInTarget = targetChange != null && targetChange.IsUpdated;
            var updatedInSource = sourceChange != null && sourceChange.IsUpdated;

            if (removedInTarget)
            {
                if (addedInSource)
                {
                    var message =
                        $"""
                        There was a conflict when merging version properties. In file {fileTargetRepoPath}, property '{propertyNameTransformer(property)}'
                        was removed in the target branch but added in the source repo.

                        We will prefer the target repo change and not add the property.
                        """;
                    _commentCollector.AddComment(message, CommentType.Information);
                }
                // we don't have to do anything since the property is removed in the repo
                // even if the property was added in the source repo, we'll take what's in the target repo
                continue;
            }

            if (removedInSource)
            {
                if (addedInTarget)
                {
                    var message =
                        $"""
                        There was a conflict when merging version properties. In file {fileTargetRepoPath}, property '{propertyNameTransformer(property)}'
                        was added in the target branch but removed in the source repo's branch.

                        We will prefer the change from the source branch and not add the property.
                        """;
                    _commentCollector.AddComment(message, CommentType.Information);
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
}

