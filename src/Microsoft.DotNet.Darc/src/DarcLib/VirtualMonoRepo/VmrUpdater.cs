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
    private readonly IVmrPatchHandler _patchHandler;
    private readonly ILogger<VmrUpdater> _logger;

    public VmrUpdater(
        IVmrDependencyTracker dependencyTracker,
        IRemoteFactory remoteFactory,
        IVersionDetailsParser versionDetailsParser,
        IVmrPatchHandler patchHandler,
        ILogger<VmrUpdater> logger,
        IVmrInfo vmrInfo)
        : base(vmrInfo, dependencyTracker, versionDetailsParser, logger)
    {
        _logger = logger;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _remoteFactory = remoteFactory;
        _patchHandler = patchHandler;
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

        string clonePath = GetClonePath(mapping);
        await _patchHandler.CloneOrFetch(mapping.DefaultRemote, targetRevision ?? mapping.DefaultRef, clonePath);

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
            var commitMessages = new StringBuilder();
            while (commitsToCopy.TryPop(out LibGit2Sharp.Commit? commit))
            {
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

        reposToUpdate.Enqueue(new(mapping, targetRevision, targetVersion));

        while (reposToUpdate.TryDequeue(out var repoToUpdate))
        {
            var mappingToUpdate = repoToUpdate.Mapping;

            _logger.LogInformation("Recursively updating dependency {repo} / {commit}",
                mappingToUpdate.Name,
                repoToUpdate.TargetRevision ?? HEAD);

            await UpdateRepository(mappingToUpdate, repoToUpdate.TargetRevision, repoToUpdate.TargetVersion, noSquash, cancellationToken);
            updatedDependencies.Add(repoToUpdate);

            foreach (var (dependency, dependencyMapping) in await GetDependencies(mappingToUpdate, cancellationToken))
            {
                if (updatedDependencies.Any(d => d.Mapping == dependencyMapping) ||
                    skippedDependencies.Any(d => d.Mapping == dependencyMapping))
                {
                    continue;
                }

                var dependencySha = GetCurrentVersion(dependencyMapping);
                if (dependencySha == dependency.Commit)
                {
                    _logger.LogDebug("Dependency {name} is already at {sha}, skipping..", dependency.Name, dependencySha);
                    skippedDependencies.Add(new(dependencyMapping, dependency.Commit, dependency.Version));
                    continue;
                }

                reposToUpdate.Enqueue(new(dependencyMapping, dependency.Commit, dependency.Version));
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
        var patches = await _patchHandler.CreatePatches(
            mapping,
            clonePath,
            fromRevision,
            toRevision,
            _vmrInfo.TmpPath,
            _vmrInfo.TmpPath,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Restore files to their individual repo states so that patches can be applied
        await _patchHandler.RestorePatchedFilesFromRepo(mapping, clonePath, fromRevision, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var patch in patches)
        {
            await _patchHandler.ApplyPatch(mapping, patch, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        _dependencyTracker.UpdateDependencyVersion(mapping, new(toRevision, targetVersion));
        Commands.Stage(new Repository(_vmrInfo.VmrPath), VmrInfo.GitInfoSourcesDir);
        Commands.Stage(new Repository(_vmrInfo.VmrPath), _vmrInfo.GetSourceManifestPath());
        cancellationToken.ThrowIfCancellationRequested();

        await _patchHandler.ApplyVmrPatches(mapping, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Commit(commitMessage, author);
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

    private record DependencyUpdate(SourceMapping Mapping, string? TargetRevision, string? TargetVersion);
}
