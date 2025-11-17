// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
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

public interface IVersionDetailsFileMerger
{
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

public class VersionDetailsFileMerger : VmrVersionFileMerger, IVersionDetailsFileMerger
{
    private readonly ILogger<VmrVersionFileMerger> _logger;
    private readonly ICommentCollector _commentCollector;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IDependencyFileManager _dependencyFileManager;

    public VersionDetailsFileMerger(
            IGitRepoFactory gitRepoFactory,
            ILogger<VmrVersionFileMerger> logger,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IDependencyFileManager dependencyFileManager,
            ICommentCollector commentCollector)
        : base(gitRepoFactory, commentCollector)
    {
        _logger = logger;
        _versionDetailsParser = versionDetailsParser;
        _dependencyFileManager = dependencyFileManager;
        _commentCollector = commentCollector;
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

        VersionFileChanges<DependencyUpdate> mergedChanges = MergeChanges(
            targetChanges,
            sourceChanges,
            SelectNewerDependencyUpdate,
            targetRepoVersionDetailsRelativePath,
            property => property);

        return await ApplyVersionDetailsChangesAsync(targetRepo, mergedChanges, mappingToApplyChanges);
    }

    private async Task<VersionDetails> GetDependencies(ILocalGitRepo repo, string commit, string relativePath)
    {
        var content = await repo.GetFileFromGitAsync(relativePath, commit);
        return content == null
            ? new VersionDetails([], null)
            : _versionDetailsParser.ParseVersionDetailsXml(content, includePinned: true);
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

        foreach ((var assetName, var addition) in changes.Additions)
        {
            if (await _dependencyFileManager.TryAddOrUpdateDependency(
                (DependencyDetail)addition.Value!,
                repo.Path,
                null!,
                versionFilesBasePath,
                versionDetailsOnly: true,
                versionDetailsPropsExists))
            {
                appliedChanges.Additions[assetName] = addition;
            }
        }

        foreach ((var assetName, var update) in changes.Updates)
        {
            if (await _dependencyFileManager.TryAddOrUpdateDependency(
                (DependencyDetail)update.Value!,
                repo.Path,
                null!,
                versionFilesBasePath,
                versionDetailsOnly: true,
                versionDetailsPropsExists,
                allowPinnedDependencyUpdate: true))
            {
                appliedChanges.Updates[assetName] = update;
            }
        }

        if (changes.HasChanges)
        {
            versionFilesBasePath ??= new UnixPath(string.Empty);

            await repo.StageAsync([versionFilesBasePath / VersionFiles.VersionDetailsXml]);

            if (versionDetailsPropsExists)
            {
                await repo.StageAsync([versionFilesBasePath / VersionFiles.VersionDetailsProps]);
            }
            else
            {
                await repo.StageAsync([versionFilesBasePath / VersionFiles.VersionsProps]);
            }
        }

        return appliedChanges;
    }

    private DependencyUpdate SelectNewerDependencyUpdate(DependencyUpdate repo1Change, DependencyUpdate repo2Change)
    {
        if (SemanticVersion.TryParse(repo1Change.To?.Version!, out var repo1Version) &&
            SemanticVersion.TryParse(repo2Change.To?.Version!, out var repo2Version))
        {
            return repo1Version > repo2Version ? repo1Change : repo2Change;
        }

        _commentCollector.AddComment(
            $"""
            A conflict was detected when merging dependency files.
            The dependency {repo1Change.To?.Name} has conflicting incomparable version values `{repo1Change.To?.Version}` and `{repo2Change.To?.Version}`.

            Please verify and/or update the dependency version manually.
            """,
            CommentType.Warning);

        return repo1Change;
    }
}

