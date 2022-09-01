// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class VmrUpdater : VmrManagerBase, IVmrUpdater
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

    /// <summary>
    /// Matches output of `git apply --numstat` which lists files contained in a patch file.
    /// Example output:
    /// 0       14      /s/vmr/src/roslyn-analyzers/eng/Versions.props
    /// -       -       /s/vmr/src/roslyn-analyzers/some-binary.dll
    /// </summary>
    private static readonly Regex GitPatchSummaryLine = new(@"^[\-0-9]+\s+[\-0-9]+\s+(?<file>[^\s]+)$", RegexOptions.Compiled);

    private readonly ILogger<VmrUpdater> _logger;
    private readonly IVmrDependencyInfo _dependencyInfo;
    private readonly IProcessManager _processManager;
    private readonly IRemoteFactory _remoteFactory;

    public VmrUpdater(
        IVmrDependencyInfo dependencyInfo,
        IProcessManager processManager,
        IRemoteFactory remoteFactory,
        IVersionDetailsParser versionDetailsParser,
        ILogger<VmrUpdater> logger,
        IVmrManagerConfiguration configuration)
        : base(dependencyInfo, processManager, remoteFactory, versionDetailsParser, logger, configuration.TmpPath)
    {
        _logger = logger;
        _dependencyInfo = dependencyInfo;
        _processManager = processManager;
        _remoteFactory = remoteFactory;
    }

    public Task UpdateRepository(
        SourceMapping mapping,
        string? targetRevision,
        string? packageVersion,
        bool noSquash,
        bool updateDependencies,
        CancellationToken cancellationToken)
    {
        return updateDependencies
            ? UpdateRepositoryRecursively(mapping, targetRevision, packageVersion, noSquash, cancellationToken)
            : UpdateRepository(mapping, targetRevision, packageVersion, noSquash, cancellationToken);
    }

    private async Task UpdateRepository(
        SourceMapping mapping,
        string? targetRevision,
        string? packageVersion,
        bool noSquash,
        CancellationToken cancellationToken)
    {
        var currentSha = await GetCurrentVersion(mapping);

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
            // TODO: https://github.com/dotnet/arcade/issues/10550 - enable synchronization between arbitrary commits
            throw new EmptySyncException($"Found no commits between {currentSha} and {targetRevision} " +
                $"when synchronizing {mapping.Name}");
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
                    commitToCopy.Sha == targetCommit.Sha ? packageVersion : null,
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
                packageVersion,
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
        string? packageVersion,
        bool noSquash,
        CancellationToken cancellationToken)
    {
        var reposToUpdate = new Queue<(SourceMapping mapping, string? targetRevision, string? packageVersion)>();
        reposToUpdate.Enqueue((mapping, targetRevision, packageVersion));

        var updatedDependencies = new HashSet<(SourceMapping mapping, string? targetRevision, string? packageVersion)>();

        while (reposToUpdate.TryDequeue(out var repoToUpdate))
        {
            var mappingToUpdate = repoToUpdate.mapping;

            _logger.LogInformation("Recursively updating dependency {repo} / {commit}",
                mappingToUpdate.Name,
                repoToUpdate.targetRevision ?? HEAD);

            await UpdateRepository(mappingToUpdate, repoToUpdate.targetRevision, repoToUpdate.packageVersion, noSquash, cancellationToken);
            updatedDependencies.Add(repoToUpdate);

            foreach (var (dependency, dependencyMapping) in await GetDependencies(mappingToUpdate, cancellationToken))
            {
                if (updatedDependencies.Any(d => d.mapping == dependencyMapping))
                {
                    continue;
                }

                var dependencySha = await GetCurrentVersion(dependencyMapping);
                if (dependencySha == dependency.Commit)
                {
                    _logger.LogDebug("Dependency {name} is already at {sha}, skipping..", dependency.Name, dependencySha);
                    continue;
                }

                reposToUpdate.Enqueue((dependencyMapping, dependency.Commit, dependency.Version));
            }
        }

        var summaryMessage = new StringBuilder();
        summaryMessage.AppendLine("Recursive update finished. Updated repositories:");

        foreach (var update in updatedDependencies)
        {
            summaryMessage.AppendLine($"  - {update.mapping.Name} / {update.targetRevision ?? HEAD}");
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
        string? packageVersion,
        string clonePath,
        string commitMessage,
        Signature author,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var patchPath = GetPatchFilePath(mapping);
        await CreatePatch(mapping, clonePath, fromRevision, toRevision, patchPath, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var info = new FileInfo(patchPath);
        if (!info.Exists)
        {
            throw new InvalidOperationException($"Failed to find the patch at {patchPath}");
        }

        await RestorePatchedFilesFromRepo(mapping, fromRevision, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (info.Length == 0)
        {
            _logger.LogInformation("No changes for {repo} in given commits (maybe only excluded files changed?)", mapping.Name);
        }
        else
        {
            await ApplyPatch(mapping, patchPath, cancellationToken);
        }

        await _dependencyInfo.UpdateDependencyVersion(mapping, toRevision, packageVersion);
        Commands.Stage(new Repository(_dependencyInfo.VmrPath), VmrDependencyInfo.GitInfoSourcesPath);
        cancellationToken.ThrowIfCancellationRequested();

        await ApplyVmrPatches(mapping, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await UpdateGitmodules(cancellationToken);
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

    /// <summary>
    /// For all files for which we have patches in VMR, restore their original version from the repo.
    /// This is because VMR contains already patched versions of these files and new updates from the repo wouldn't apply.
    /// </summary>
    /// <param name="mapping">Mapping</param>
    /// <param name="originalRevision">Revision from which we were updating</param>
    private async Task RestorePatchedFilesFromRepo(SourceMapping mapping, string originalRevision, CancellationToken cancellationToken)
    {
        if (!mapping.VmrPatches.Any())
        {
            return;
        }

        _logger.LogInformation("Restoring files with patches for {mappingName}..", mapping.Name);

        // We checkout the clone to the given revision once for all its patches
        var clonePath = GetClonePath(mapping);
        if (!Directory.Exists(clonePath))
        {
            await CloneOrPull(mapping);
        }

        var localRepo = new LocalGitClient(_processManager.GitExecutable, _logger);
        localRepo.Checkout(clonePath, originalRevision);

        var repoSourcesPath = _dependencyInfo.GetRepoSourcesPath(mapping);

        foreach (var patch in mapping.VmrPatches)
        {
            _logger.LogDebug("Processing VMR patch `{patch}`..", patch);

            foreach (var patchedFile in await GetFilesInPatch(clonePath, patch, cancellationToken))
            {
                // git always works with forward slashes (even on Windows)
                string relativePath = Path.DirectorySeparatorChar != '/'
                    ? patchedFile.Replace('/', Path.DirectorySeparatorChar)
                    : patchedFile;

                var originalFile = Path.Combine(clonePath, relativePath);
                var destination = Path.Combine(repoSourcesPath, relativePath);

                _logger.LogDebug("Restoring file `{originalFile}` to `{destination}`..", originalFile, destination);

                // Copy old revision to VMR
                File.Copy(originalFile, destination, overwrite: true);
            }
        }

        // Stage the restored files (all future patches are applied to index directly)
        using var repository = new Repository(_dependencyInfo.VmrPath);
        Commands.Stage(repository, repoSourcesPath);

        _logger.LogDebug("Files from VMR patches for {mappingName} restored", mapping.Name);
    }

    /// <summary>
    /// Resolves a list of all files that are part of a given patch diff.
    /// </summary>
    /// <param name="repoPath">Path (to the repo) the patch applies onto</param>
    /// <param name="patchPath">Path to the patch file</param>
    /// <returns>List of all files (paths relative to repo's root) that are part of a given patch diff</returns>
    private async Task<IReadOnlyCollection<string>> GetFilesInPatch(string repoPath, string patchPath, CancellationToken cancellationToken)
    {
        var result = await _processManager.ExecuteGit(repoPath, new[] { "apply", "--numstat", patchPath }, cancellationToken);
        result.ThrowIfFailed($"Failed to enumerate files from a patch at `{patchPath}`");

        var files = new List<string>();
        foreach (var line in result.StandardOutput.Split(Environment.NewLine))
        {
            var match = GitPatchSummaryLine.Match(line);
            if (match.Success)
            {
                files.Add(match.Groups["file"].Value);
            }
        }

        return files;
    }

    private async Task<string> GetCurrentVersion(SourceMapping mapping)
    {
        var version = await _dependencyInfo.GetDependencyVersion(mapping);

        if (!version.HasValue)
        {
            throw new InvalidOperationException($"Missing tag file for {mapping.Name} - please initialize the individual repo first");
        }

        return version.Value.Sha;
    }
}
