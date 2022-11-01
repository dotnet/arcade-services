// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
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
    // Message shown when synchronizing a single commit
    private const string SingleCommitMessage =
        $$"""
        [{name}] Sync {newShaShort}: {commitMessage}

        Original commit: {remote}/commit/{newSha}
        
        {{AUTOMATION_COMMIT_TAG}}
        """;

    // Message shown when synchronizing multiple commits as one
    private const string SquashCommitMessage =
        $$"""
        [{name}] Sync {oldShaShort} → {newShaShort}
        Diff: {remote}/compare/{oldSha}..{newSha}
        
        From: {remote}/commit/{oldSha}
        To: {remote}/commit/{newSha}
        
        Commits:
        {commitMessage}
        
        {{AUTOMATION_COMMIT_TAG}}
        """;

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrUpdater> _logger;

    public VmrUpdater(
        IVmrDependencyTracker dependencyTracker,
        IRemoteFactory remoteFactory,
        IVersionDetailsParser versionDetailsParser,
        IRepositoryCloneManager cloneManager,
        IVmrPatchHandler patchHandler,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        ILocalGitRepo localGitClient,
        IFileSystem fileSystem,
        ILogger<VmrUpdater> logger,
        IVmrInfo vmrInfo)
        : base(vmrInfo, dependencyTracker, versionDetailsParser, thirdPartyNoticesGenerator, localGitClient, logger)
    {
        _logger = logger;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _remoteFactory = remoteFactory;
        _cloneManager = cloneManager;
        _patchHandler = patchHandler;
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
            : UpdateRepository(mapping, targetRevision, targetVersion, noSquash, cancellationToken);
    }

    private async Task UpdateRepository(
        SourceMapping mapping,
        string? targetRevision,
        string? targetVersion,
        bool noSquash,
        CancellationToken cancellationToken)
    {
        var currentSha = GetCurrentVersion(mapping);

        if (targetRevision is null || targetRevision == HEAD && !await HasRemoteUpdates(mapping, currentSha))
        {
            throw new EmptySyncException($"No new remote changes detected for {mapping.Name}");
        }

        _logger.LogInformation("Synchronizing {name} from {current} to {repo}@{revision}{oneByOne}",
            mapping.Name, currentSha, mapping.DefaultRemote, targetRevision ?? HEAD, noSquash ? " one commit at a time" : string.Empty);

        string clonePath = await _cloneManager.PrepareClone(mapping.DefaultRemote, targetRevision ?? mapping.DefaultRef, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        LibGit2Sharp.Commit currentCommit;
        LibGit2Sharp.Commit targetCommit;
        using (var clone = new Repository(clonePath))
        {
            currentCommit = GetCommit(clone, currentSha);
            targetCommit = GetCommit(clone, targetRevision);
        }

        targetRevision = targetCommit.Id.Sha;

        if (currentSha == targetRevision)
        {
            _logger.LogInformation("No new commits found to synchronize");
            return;
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

        // When we go one by one, we basically "copy" the commits.
        // Let's do the same in case we don't explicitly go one by one but we only have one commit..
        if ((noSquash || commitsToCopy.Count == 1) && !arbitraryCommits)
        {
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

                await UpdateRepoToRevision(
                    mapping,
                    currentSha,
                    commitToCopy.Sha,
                    commitToCopy.Sha == targetCommit.Sha ? targetVersion : null,
                    clonePath,
                    message,
                    commitToCopy.Author,
                    cancellationToken);

                currentSha = commitToCopy.Id.Sha;
            }
        }
        else
        {
            // We squash commits and list them in the message
            var commitMessages = new StringBuilder();
            var commitCount = 1;
            bool commitLimitHit = false;
            while (!commitLimitHit && commitsToCopy.TryPop(out LibGit2Sharp.Commit? commit))
            {
                // Do not list over 23 commits in the message
                // If there are more, list first 20
                commitCount++;          
                if (commitCount == 20 && commitsToCopy.Count > 3)
                {
                    commitLimitHit = true;
                    commitMessages.AppendLine($"  [... commit list trimmed ...]");
                    continue;
                }

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

            await UpdateRepoToRevision(
                mapping,
                currentSha,
                targetRevision,
                targetVersion,
                clonePath,
                message,
                DotnetBotCommitSignature,
                cancellationToken);
        }
    }

    /// <summary>
    /// Updates a repository and all of it's dependencies recursively starting with a given mapping.
    /// Always updates to the first version found per repository in the dependency tree.
    /// </summary>
    private async Task UpdateRepositoryRecursively(
        SourceMapping mapping,
        string? targetRevision,
        string? targetVersion,
        bool noSquash,
        CancellationToken cancellationToken)
    {
        var reposToUpdate = new Queue<DependencyUpdate>();
        var updatedDependencies = new HashSet<DependencyUpdate>();
        var skippedDependencies = new HashSet<DependencyUpdate>();

        reposToUpdate.Enqueue(new DependencyUpdate(mapping, targetRevision, targetVersion, null));

        while (reposToUpdate.TryDequeue(out var repoToUpdate))
        {
            var mappingToUpdate = repoToUpdate.Mapping;
            var currentSha = GetCurrentVersion(mappingToUpdate);

            if (repoToUpdate.Parent is null)
            {
                _logger.LogInformation("Starting recursive update for {repo} / {from} → {to}",
                    mappingToUpdate.Name,
                    currentSha,
                    repoToUpdate.TargetRevision ?? HEAD);
            }
            else
            {
                _logger.LogInformation("Recursively updating {parent}'s dependency {repo} / {from} → {to}",
                    repoToUpdate.Parent,
                    mappingToUpdate.Name,
                    currentSha,
                    repoToUpdate.TargetRevision ?? HEAD);
            }

            await UpdateRepository(mappingToUpdate, repoToUpdate.TargetRevision, repoToUpdate.TargetVersion, noSquash, cancellationToken);
            updatedDependencies.Add(repoToUpdate);

            foreach (var (dependency, dependencyMapping) in await GetDependencies(mappingToUpdate, cancellationToken))
            {
                if (updatedDependencies.Any(d => d.Mapping == dependencyMapping) ||
                    skippedDependencies.Any(d => d.Mapping == dependencyMapping))
                {
                    continue;
                }

                var dependencyUpdate = new DependencyUpdate(
                    Mapping: dependencyMapping,
                    TargetRevision: dependency.Commit,
                    TargetVersion: dependency.Version,
                    Parent: mappingToUpdate.Name);

                var dependencySha = GetCurrentVersion(dependencyMapping);
                if (dependencySha == dependency.Commit)
                {
                    _logger.LogDebug("Dependency {name} is already at {sha}, skipping..", dependency.Name, dependencySha);
                    skippedDependencies.Add(dependencyUpdate);
                    continue;
                }

                reposToUpdate.Enqueue(dependencyUpdate);
            }
        }

        var summaryMessage = new StringBuilder();
        summaryMessage.AppendLine("Recursive update finished. Updated repositories:");

        foreach (var update in updatedDependencies)
        {
            summaryMessage.AppendLine($"  - {update.Mapping.Name} / {update.TargetRevision ?? HEAD}");
        }

        _logger.LogInformation("{summary}", summaryMessage);
    }

    /// <summary>
    /// Synchronizes given repo in VMR onto given revision.
    /// </summary>
    private async Task UpdateRepoToRevision(
        SourceMapping mapping,
        string fromRevision,
        string toRevision,
        string? targetVersion,
        string clonePath,
        string commitMessage,
        Signature author,
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
        Commands.Stage(new Repository(_vmrInfo.VmrPath), new[]
        {
            VmrInfo.GitInfoSourcesDir,
            _vmrInfo.GetSourceManifestPath()
        });

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var (affectedMapping, patchPath) in vmrPatchesToRestore)
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

        await UpdateThirdPartyNotices(cancellationToken);

        Commit(commitMessage, author);
    }

    private async Task<IReadOnlyCollection<(SourceMapping Mapping, string Path)>> RestoreVmrPatchedFiles(SourceMapping mapping, string clonePath, IReadOnlyCollection<VmrIngestionPatch> patches, CancellationToken cancellationToken)
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
        string clonePath,
        IReadOnlyCollection<VmrIngestionPatch> patches,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting a list of VMR patches to restore for {repo} before we ingest new changes...", updatedMapping.Name);

        var patchesToRestore = new List<(SourceMapping Mapping, string Path)>();

        // Always restore all patches belonging to the currently updated mapping
        patchesToRestore.AddRange(_patchHandler.GetVmrPatches(updatedMapping).Select(patch => (updatedMapping, patch)));

        // If we are not updating the mapping that the VMR patches come from, we're done
        if (_vmrInfo.PatchesPath == null || !_vmrInfo.PatchesPath.StartsWith(_vmrInfo.GetRepoSourcesPath(updatedMapping)))
        {
            return patchesToRestore;
        }

        _logger.LogInformation("Repo {repo} contains VMR patches, checking which VMR patches have changes...", updatedMapping.Name);

        // Check which files are modified by every of the patches that bring new changed into the VMR
        foreach (var patch in patches)
        {
            var patchedFiles = await _patchHandler.GetPatchedFiles(clonePath, patch.Path, cancellationToken);
            var affectedPatches = patchedFiles
                .Select(path => path.Replace('/', _fileSystem.DirectorySeparatorChar))
                .Select(path => _fileSystem.PathCombine(_vmrInfo.GetRepoSourcesPath(updatedMapping), path))
                .Where(path => path.StartsWith(_vmrInfo.PatchesPath) && path.EndsWith(".patch"));

            foreach (var affectedPatch in affectedPatches)
            {
                // patch is in the folder named as the mapping for which it is applied
                var affectedMappping = affectedPatch.Split(_fileSystem.DirectorySeparatorChar)[^2];
                
                _logger.LogInformation("Detected a change of a VMR patch {patch} for {repo}", affectedPatch, affectedMappping);
                patchesToRestore.Add((_dependencyTracker.Mappings.First(m => m.Name == affectedMappping), affectedPatch));
            }
        }

        return patchesToRestore
            .DistinctBy(p => p.Path) // Make sure we don't restore the same patch twice
            .ToArray();
    }

    /// <summary>
    /// Checks remotely if there are any newer commits (whether it even makes sense to clone).
    /// </summary>
    private async Task<bool> HasRemoteUpdates(SourceMapping mapping, string currentSha)
    {
        var remoteRepo = await _remoteFactory.GetRemoteAsync(mapping.DefaultRemote, _logger);
        var lastCommit = await remoteRepo.GetLatestCommitAsync(mapping.DefaultRemote, mapping.DefaultRef);
        return !lastCommit.Equals(currentSha, StringComparison.InvariantCultureIgnoreCase);
    }

    private string GetCurrentVersion(SourceMapping mapping)
    {
        var version = _dependencyTracker.GetDependencyVersion(mapping);

        if (version is null)
        {
            throw new InvalidOperationException($"Repository {mapping.Name} has not been initialized yet");
        }

        return version.Sha;
    }

    private record DependencyUpdate(
        SourceMapping Mapping,
        string? TargetRevision,
        string? TargetVersion,
        string? Parent);
}
