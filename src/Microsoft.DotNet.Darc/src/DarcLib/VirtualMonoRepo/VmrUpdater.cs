// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is able to update an individual repository within the VMR from one commit to another.
/// It creates git diffs while adhering to cloaking rules, accommodating for patched files, resolving submodules.
/// It can also update other repositories recursively based on the dependencies stored in Version.Details.xml.
/// </summary>
public class VmrUpdater : VmrManagerBase, IVmrUpdater
{
    // Message used when synchronizing a single commit
    private const string SingleCommitMessage =
        $$"""
        [{name}] Sync {newShaShort}: {commitMessage}

        Original commit: {remote}/commit/{newSha}
        
        {{AUTOMATION_COMMIT_TAG}}
        """;

    // Message used when synchronizing multiple commits as one
    private const string SquashCommitMessage =
        $$"""
        [{name}] Sync {oldShaShort}{{Arrow}}{newShaShort}
        Diff: {remote}/compare/{oldSha}..{newSha}
        
        From: {remote}/commit/{oldSha}
        To: {remote}/commit/{newSha}
        
        Commits:
        {commitMessage}
        
        {{AUTOMATION_COMMIT_TAG}}
        """;

    // Message used when finalizing the sync with a merge commit
    private const string MergeCommitMessage =
        $$"""
        [Recursive sync] {name} / {oldShaShort}{{Arrow}}{newShaShort}
        
        Updated repositories:
        {commitMessage}
        
        {{AUTOMATION_COMMIT_TAG}}
        """;

    // Character we use in the commit messages to indicate the change
    private const string Arrow = " → ";

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IReadmeComponentListGenerator _readmeComponentListGenerator;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrUpdater> _logger;

    public VmrUpdater(
        IVmrDependencyTracker dependencyTracker,
        IVersionDetailsParser versionDetailsParser,
        IRepositoryCloneManager cloneManager,
        IVmrPatchHandler patchHandler,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        IReadmeComponentListGenerator readmeComponentListGenerator,
        ILocalGitRepo localGitClient,
        IGitFileManagerFactory gitFileManagerFactory,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger,
        ISourceManifest sourceManifest,
        IVmrInfo vmrInfo)
        : base(vmrInfo, sourceManifest, dependencyTracker, versionDetailsParser, thirdPartyNoticesGenerator, localGitClient, gitFileManagerFactory, fileSystem, logger)
    {
        _logger = logger;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _cloneManager = cloneManager;
        _patchHandler = patchHandler;
        _readmeComponentListGenerator = readmeComponentListGenerator;
        _fileSystem = fileSystem;
    }

    public Task UpdateRepository(
        SourceMapping mapping,
        string? targetRevision,
        string? targetVersion,
        bool noSquash,
        bool updateDependencies,
        CancellationToken cancellationToken)
    {
        return updateDependencies
            ? UpdateRepositoryRecursively(mapping, targetRevision, targetVersion, noSquash, cancellationToken)
            : UpdateRepositoryInternal(mapping, targetRevision, targetVersion, noSquash, reapplyVmrPatches: true, cancellationToken);
    }

    private async Task<IReadOnlyCollection<(SourceMapping Mapping, string Path)>> UpdateRepositoryInternal(
        SourceMapping mapping,
        string? targetRevision,
        string? targetVersion,
        bool noSquash,
        bool reapplyVmrPatches,
        CancellationToken cancellationToken)
    {
        var currentSha = GetCurrentVersion(mapping);

        _logger.LogInformation("Synchronizing {name} from {current} to {repo} / {revision}{oneByOne}",
            mapping.Name, currentSha, mapping.DefaultRemote, targetRevision ?? HEAD, noSquash ? " one commit at a time" : string.Empty);

        LocalPath clonePath = await _cloneManager.PrepareClone(mapping.DefaultRemote, targetRevision ?? mapping.DefaultRef, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        targetRevision = GetShaForRef(clonePath, targetRevision);

        if (currentSha == targetRevision)
        {
            _logger.LogInformation("No new commits found to synchronize");
            return Array.Empty<(SourceMapping Mapping, string Path)>();
        }

        using var repo = new Repository(clonePath);
        ICommitLog commits = repo.Commits.QueryBy(new CommitFilter
        {
            FirstParentOnly = true,
            IncludeReachableFrom = targetRevision,
        });

        // Will contain SHAs in the order as we want to apply them
        var commitsToCopy = new Stack<LibGit2Sharp.Commit>();

        foreach (var commit in commits)
        {
            // Target revision goes first
            if (commit.Sha.StartsWith(targetRevision))
            {
                commitsToCopy.Push(commit);
                continue;
            }

            // If we reach current commit, stop adding
            if (commit.Sha.StartsWith(currentSha))
            {
                break;
            }

            // Otherwise add anything in between
            if (commitsToCopy.Count > 0)
            {
                commitsToCopy.Push(commit);
            }
        }

        // When no path between two commits is found, force synchronization between arbitrary commits
        // For this case, do not copy the commit with the same author so it doesn't seem like one commit
        // from the individual repo
        bool arbitraryCommits = commitsToCopy.Count == 0;
        if (arbitraryCommits)
        {
            commitsToCopy.Push(repo.Lookup<LibGit2Sharp.Commit>(targetRevision));
        }

        // When we don't need to copy the commits one by one and we have more than 1 to mirror,
        // do them in bulk
        if (!noSquash && commitsToCopy.Count != 1 || arbitraryCommits)
        {
            // We squash commits and list them in the message
            var commitMessages = new StringBuilder();
            var commitCount = 0;
            while (commitsToCopy.TryPop(out LibGit2Sharp.Commit? commit))
            {
                // Do not list over 23 commits in the message
                // If there are more, list first 20         
                if (commitCount == 20 && commitsToCopy.Count > 3)
                {
                    commitMessages.AppendLine("  [... commit list trimmed ...]");
                    break;
                }

                commitCount++;
                commitMessages
                    .AppendLine($"  - {commit.MessageShort}")
                    .AppendLine($"    {mapping.DefaultRemote}/commit/{commit.Id.Sha}");
            }

            var message = PrepareCommitMessage(
                SquashCommitMessage,
                mapping,
                currentSha,
                targetRevision,
                commitMessages.ToString());

            return await UpdateRepoToRevision(
                mapping,
                currentSha,
                targetRevision,
                targetVersion,
                clonePath,
                message,
                DotnetBotCommitSignature,
                reapplyVmrPatches,
                cancellationToken);
        }

        var vmrPatchesToRestore = new List<(SourceMapping Mapping, string Path)>();
        while (commitsToCopy.TryPop(out LibGit2Sharp.Commit? commitToCopy))
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Updating {repo} from {current} to {next}..",
                mapping.Name, DarcLib.Commit.GetShortSha(currentSha), DarcLib.Commit.GetShortSha(commitToCopy.Id.Sha));

            var message = PrepareCommitMessage(
                SingleCommitMessage,
                mapping,
                currentSha,
                commitToCopy.Id.Sha,
                commitToCopy.Message);

            var patches = await UpdateRepoToRevision(
                mapping,
                currentSha,
                commitToCopy.Sha,
                commitToCopy.Sha == targetRevision ? targetVersion : null,
                clonePath,
                message,
                commitToCopy.Author,
                reapplyVmrPatches,
                cancellationToken);

            vmrPatchesToRestore.AddRange(vmrPatchesToRestore);

            currentSha = commitToCopy.Id.Sha;
        }

        return vmrPatchesToRestore.DistinctBy(patch => patch.Path).ToImmutableList();
    }

    /// <summary>
    /// Updates a repository and all of it's dependencies recursively starting with a given mapping.
    /// Always updates to the first version found per repository in the dependency tree.
    /// </summary>
    private async Task UpdateRepositoryRecursively(
        SourceMapping rootMapping,
        string? targetRevision,
        string? targetVersion,
        bool noSquash,
        CancellationToken cancellationToken)
    {
        targetRevision ??= HEAD;

        string originalRootSha = GetCurrentVersion(rootMapping);
        _logger.LogInformation("Recursive update for {repo} / {from}{arrow}{to}",
            rootMapping.Name,
            DarcLib.Commit.GetShortSha(originalRootSha),
            Arrow,
            targetRevision);

        var rootUpdate = new DependencyUpdate(rootMapping, rootMapping.DefaultRemote, targetRevision, targetVersion, null);
        var updates = (await GetAllDependencies(rootUpdate, cancellationToken)).ToList();

        var extraneousMappings = _dependencyTracker.Mappings
            .Where(mapping => !updates.Any(update => update.Mapping == mapping))
            .Select(mapping => mapping.Name);

        if (extraneousMappings.Any())
        {
            var separator = $"{Environment.NewLine}  - ";
            _logger.LogWarning($"The following mappings do not appear in current update's dependency tree:{separator}{{extraMappings}}",
                string.Join(separator, extraneousMappings));
        }

        // When we synchronize in bulk, we do it in a separate branch that we then merge into the main one
        var workBranch = CreateWorkBranch($"sync/{rootMapping.Name}/{DarcLib.Commit.GetShortSha(GetCurrentVersion(rootMapping))}-{targetRevision}");

        // Collection of all affected VMR patches we will need to restore after the sync
        var vmrPatchesToReapply = new List<(SourceMapping Mapping, string Path)>();

        // Dependencies that were already updated during this run
        var updatedDependencies = new HashSet<DependencyUpdate>();

        foreach (DependencyUpdate update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string currentSha;
            try
            {
                currentSha = GetCurrentVersion(update.Mapping);
            }
            catch (RepositoryNotInitializedException)
            {
                _logger.LogWarning("Dependency {repo} has not been initialized in the VMR yet, repository will be initialized",
                    update.Mapping.Name);

                currentSha = Constants.EmptyGitObject;
                _dependencyTracker.UpdateDependencyVersion(update.Mapping, new VmrDependencyVersion(currentSha, null));
            }

            if (currentSha == update.TargetRevision)
            {
                _logger.LogDebug("Dependency {name} is already at {sha}, skipping..", update.Mapping.Name, currentSha);
                continue;
            }

            if (update.Parent is not null)
            {
                _logger.LogInformation("Recursively updating {parent}'s dependency {repo} / {from}{arrow}{to}",
                    update.Parent,
                    update.Mapping.Name,
                    currentSha,
                    Arrow,
                    update.TargetRevision);
            }

            var patchesToReapply = await UpdateRepositoryInternal(
                update.Mapping,
                update.TargetRevision,
                update.TargetVersion,
                noSquash,
                false,
                cancellationToken);

            vmrPatchesToReapply.AddRange(patchesToReapply);

            updatedDependencies.Add(update with
            {
                // We the SHA again because original target could have been a branch name
                TargetRevision = GetCurrentVersion(update.Mapping),

                // We also store the original version so that later we use it in the commit message
                TargetVersion = currentSha,
            });
        }

        string finalRootSha = GetCurrentVersion(rootMapping);
        var summaryMessage = new StringBuilder();

        foreach (var update in updatedDependencies)
        {
            var fromShort = DarcLib.Commit.GetShortSha(update.TargetVersion);
            var toShort = DarcLib.Commit.GetShortSha(update.TargetRevision);
            summaryMessage
                .AppendLine($"  - {update.Mapping.Name} / {fromShort}{Arrow}{toShort}")
                .AppendLine($"    {update.Mapping.DefaultRemote}/compare/{update.TargetVersion}..{update.TargetRevision}");
        }

        if (vmrPatchesToReapply.Any())
        {
            await ReapplyVmrPatches(vmrPatchesToReapply.DistinctBy(p => p.Path).ToArray(), cancellationToken);
            Commit("[VMR patches] Re-apply VMR patches", DotnetBotCommitSignature);
        }

        var commitMessage = PrepareCommitMessage(
            MergeCommitMessage,
            rootMapping,
            originalRootSha,
            finalRootSha,
            summaryMessage.ToString());
        
        workBranch.MergeBack(commitMessage);

        _logger.LogInformation("Recursive update for {repo} finished.{newLine}{message}",
            rootMapping.Name,
            Environment.NewLine,
            summaryMessage);
    }

    /// <summary>
    /// Synchronizes given repo in VMR onto given revision.
    /// </summary>
    private async Task<IReadOnlyCollection<(SourceMapping Mapping, string Path)>> UpdateRepoToRevision(
        SourceMapping mapping,
        string fromRevision,
        string toRevision,
        string? targetVersion,
        LocalPath clonePath,
        string commitMessage,
        Signature author,
        bool reapplyVmrPatches,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            mapping,
            clonePath,
            fromRevision,
            toRevision,
            _vmrInfo.TmpPath,
            _vmrInfo.TmpPath,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Get a list of patches that need to be reverted for this update so that repo changes can be applied
        // This includes all patches that are also modified by the current change
        // (happens when we update repo from which the VMR patches come)
        IReadOnlyCollection<(SourceMapping Mapping, string Path)> vmrPatchesToRestore = await RestoreVmrPatchedFiles(
            mapping,
            clonePath,
            patches,
            cancellationToken);

        foreach (var patch in patches)
        {
            await _patchHandler.ApplyPatch(mapping, patch, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        _dependencyTracker.UpdateDependencyVersion(mapping, new VmrDependencyVersion(toRevision, targetVersion));
        await _readmeComponentListGenerator.UpdateReadme();
        
        Commands.Stage(new Repository(_vmrInfo.VmrPath), new string[]
        {
            VmrInfo.ReadmeFileName,
            VmrInfo.GitInfoSourcesDir,
            _vmrInfo.GetSourceManifestPath()
        });

        cancellationToken.ThrowIfCancellationRequested();

        if (reapplyVmrPatches)
        {
            await ReapplyVmrPatches(vmrPatchesToRestore.DistinctBy(p => p.Path).ToArray(), cancellationToken);
        }

        await UpdateThirdPartyNotices(cancellationToken);

        Commit(commitMessage, author);

        return vmrPatchesToRestore;
    }

    private async Task<IReadOnlyCollection<(SourceMapping Mapping, string Path)>> RestoreVmrPatchedFiles(
        SourceMapping mapping,
        LocalPath clonePath,
        IReadOnlyCollection<VmrIngestionPatch> patches,
        CancellationToken cancellationToken)
    {
        var vmrPatchesToRestore = await GetVmrPatchesToRestore(mapping, clonePath, patches, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (vmrPatchesToRestore.Count == 0)
        {
            return vmrPatchesToRestore;
        }
        
        _logger.LogInformation("Found {count} VMR patches to restore. Restoring original files...", vmrPatchesToRestore.Count);

        foreach (var (affectedMapping, patchPath) in vmrPatchesToRestore)
        {
            if (!_fileSystem.FileExists(patchPath))
            {
                // Patch is being added, so it doesn't exist yet
                _logger.LogDebug("Not applying {patch} as will be added", patchPath);
                continue;
            }

            var versionToRestoreFrom = _dependencyTracker.GetDependencyVersion(affectedMapping);
            if (versionToRestoreFrom == null)
            {
                _logger.LogInformation("Skipping VMR patches for {repo} as it hasn't been initialized yet", affectedMapping.Name);
                continue;
            }

            var repoToRestoreFrom = await _cloneManager.PrepareClone(
                affectedMapping.DefaultRemote,
                versionToRestoreFrom.Sha,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            await _patchHandler.RestoreFilesFromPatch(
                affectedMapping,
                repoToRestoreFrom,
                patchPath,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
        }

        _logger.LogInformation("Files affected by VMR patches restored");

        return vmrPatchesToRestore;
    }

    /// <summary>
    /// Gets a list of VMR patches that need to be reverted for a given mapping update so that repo changes can be applied.
    /// Usually, this just returns all VMR patches for that given mapping (e.g. for the aspnetcore returns all aspnetcore only patches).
    /// 
    /// One exception is when the updated mapping is the one that the VMR patches come from into the VMR (e.g. dotnet/installer).
    /// In this case, we also check which VMR patches are modified by the change and we also returns those.
    /// Examples:
    ///   - An aspnetcore VMR patch is removed from installer - we must remove it from the files it is applied to in the VMR.
    ///   - A new version of patch is synchronized from installer - we must remove the old version and apply the new.
    /// </summary>
    /// <param name="updatedMapping">Currently synchronized mapping</param>
    /// <param name="patches">Patches of currently synchronized changes</param>
    private async Task<IReadOnlyCollection<(SourceMapping Mapping, string Path)>> GetVmrPatchesToRestore(
        SourceMapping updatedMapping,
        LocalPath clonePath,
        IReadOnlyCollection<VmrIngestionPatch> patches,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting a list of VMR patches to restore for {repo} before we ingest new changes...", updatedMapping.Name);

        var patchesToRestore = new List<(SourceMapping Mapping, string Path)>();

        // Always restore all patches belonging to the currently updated mapping
        patchesToRestore.AddRange(_patchHandler.GetVmrPatches(updatedMapping).Select(patch => (updatedMapping, patch)));

        // If we are not updating the mapping that the VMR patches come from, we're done
        if (_vmrInfo.PatchesPath == null || !_vmrInfo.PatchesPath.StartsWith(VmrInfo.GetRelativeRepoSourcesPath(updatedMapping)))
        {
            return patchesToRestore;
        }

        _logger.LogInformation("Repo {repo} contains VMR patches, checking which VMR patches have changes...", updatedMapping.Name);

        // Check which files are modified by every of the patches that bring new changed into the VMR
        foreach (var patch in patches)
        {
            var patchedFiles = await _patchHandler.GetPatchedFiles(clonePath, patch.Path, cancellationToken);
            var affectedPatches = patchedFiles
                .Select(path => VmrInfo.GetRelativeRepoSourcesPath(updatedMapping) / path)
                .Where(path => path.Path.StartsWith(_vmrInfo.PatchesPath) && path.Path.EndsWith(".patch"))
                .Select(path => _vmrInfo.VmrPath / path);

            foreach (var affectedPatch in affectedPatches)
            {
                // patch is in the folder named as the mapping for which it is applied
                var affectedMappping = affectedPatch.Path.Split(_fileSystem.DirectorySeparatorChar)[^2];
                
                _logger.LogInformation("Detected a change of a VMR patch {patch} for {repo}", affectedPatch, affectedMappping);
                patchesToRestore.Add((_dependencyTracker.Mappings.First(m => m.Name == affectedMappping), affectedPatch));
            }
        }

        return patchesToRestore;
    }

    private async Task ReapplyVmrPatches(
        IReadOnlyCollection<(SourceMapping Mapping, string Path)> patches,
        CancellationToken cancellationToken)
    {
        if (patches.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Re-applying {count} VMR patch{s}...",
            patches.Count,
            patches.Count > 1 ? "es" : string.Empty);

        foreach (var (affectedMapping, patchPath) in patches)
        {
            if (!_fileSystem.FileExists(patchPath))
            {
                // Patch was removed, so it doesn't exist anymore
                _logger.LogDebug("Not re-applying {patch} as it was removed", patchPath);
                continue;
            }

            // Re-apply VMR patch back
            await _patchHandler.ApplyPatch(affectedMapping, patchPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private string GetCurrentVersion(SourceMapping mapping)
    {
        var version = _dependencyTracker.GetDependencyVersion(mapping);

        if (version is null)
        {
            throw new RepositoryNotInitializedException($"Repository {mapping.Name} has not been initialized yet");
        }

        return version.Sha;
    }

    private class RepositoryNotInitializedException : Exception
    {
        public RepositoryNotInitializedException(string message)
            : base(message)
        {
        }
    }
}
