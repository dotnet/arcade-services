// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class VmrUpdater : VmrManagerBase
{
    // Message shown when synchronizing a single commit
    private const string SingleCommitMessage =
        """
        [{name}] Sync {newShaShort}: {commitMessage}

        Original commit: {remote}/commit/{newSha}
        """;

    // Message shown when synchronizing multiple commits as one
    private const string SquashCommitMessage =
        """
        [{name}] Sync {oldShaShort} → {newShaShort}
        Diff: {remote}/compare/{oldSha}..{newSha}
        
        From: {remote}/commit/{oldSha}
        To: {remote}/commit/{newSha}
        
        Commits:
        {commitMessage}
        """;

    private readonly ILogger<VmrUpdater> _logger;
    private readonly IRemoteFactory _remoteFactory;

    public VmrUpdater(
        IProcessManager processManager,
        IRemoteFactory remoteFactory,
        ILogger<VmrUpdater> logger,
        IReadOnlyCollection<SourceMapping> mappings,
        string vmrPath,
        string tmpPath)
        : base(processManager, remoteFactory, logger, mappings, vmrPath, tmpPath)
    {
        _logger = logger;
        _remoteFactory = remoteFactory;
    }

    public async Task UpdateVmr(SourceMapping mapping, string? targetRevision, bool noSquash, CancellationToken cancellationToken)
    {
        var tagFile = GetTagFilePath(mapping);
        string currentSha;
        try
        {
            currentSha = File.ReadAllText(tagFile).Trim();
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException($"Missing tag file for {mapping.Name} - please init the individual repo first");
        }

        if (!await HasRemoteUpdates(mapping, currentSha))
        {
            throw new EmptySyncException($"No new remote changes detected for {mapping.Name}");
        }

        _logger.LogInformation("Synchronizing {name} from {current} to {repo}@{revision}{oneByOne}",
            mapping.Name, currentSha, mapping.DefaultRemote, targetRevision ?? HEAD, noSquash ? " one commit at a time" : string.Empty);

        var clonePath = await CloneOrPull(mapping);

        cancellationToken.ThrowIfCancellationRequested();

        using var clone = new Repository(clonePath);
        var currentCommit = GetCommit(clone, currentSha);
        var targetCommit = GetCommit(clone, targetRevision);

        targetRevision = targetCommit.Id.Sha;

        if (currentSha == targetRevision)
        {
            _logger.LogInformation("No new commits found to synchronize");
            return;
        }

        if (currentCommit.Committer.When > targetCommit.Committer.When)
        {
            throw new InvalidOperationException($"Target revision {targetRevision} is older than current ({currentSha})! " +
                $"Synchronizing backwards is not allowed");
        }

        using var repo = new Repository(clonePath);
        ICommitLog commits = repo.Commits.QueryBy(new CommitFilter
        {
            FirstParentOnly = true,
            IncludeReachableFrom = mapping.DefaultRef,
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

        if (commitsToCopy.Count == 0)
        {
            throw new EmptySyncException($"Found no commits between {currentSha} and {targetRevision} when synchronizing {mapping.Name}");
        }

        // When we go one by one, we basically "copy" the commits.
        // Let's do the same in case we don't explicitly go one by one but we only have one commit..
        if (noSquash || commitsToCopy.Count == 1)
        {
            while (commitsToCopy.TryPop(out LibGit2Sharp.Commit? commitToCopy))
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation("Updating {repo} from {current} to {next}..",
                    mapping.Name, ShortenId(currentSha), ShortenId(commitToCopy.Id.Sha));

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
                    .AppendLine($"    {mapping.DefaultRemote}/commit/{targetRevision}");
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
                clonePath,
                message,
                DotnetBotCommitSignature,
                cancellationToken);
        }
    }

    /// <summary>
    /// Synchronizes given repo in VMR onto given revision.
    /// </summary>
    private async Task UpdateRepoToRevision(
        SourceMapping mapping,
        string fromRevision,
        string toRevision,
        string clonePath,
        string commitMessage,
        Signature author,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var patchPath = GetPatchFilePath(mapping);
        await CreatePatch(mapping, clonePath, fromRevision, toRevision, patchPath);

        cancellationToken.ThrowIfCancellationRequested();

        var info = new FileInfo(patchPath);
        if (!info.Exists)
        {
            throw new InvalidOperationException($"Failed to find the patch at {patchPath}");
        }

        if (info.Length == 0)
        {
            _logger.LogInformation("No changes for {repo} in given commits (maybe only excluded files changed?)", mapping.Name);
        }
        else
        {
            await ApplyPatch(mapping, patchPath);
        }

        await TagRepo(mapping, toRevision);

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
}
