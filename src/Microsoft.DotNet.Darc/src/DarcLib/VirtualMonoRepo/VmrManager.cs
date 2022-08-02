// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

public interface IVmrManager
{
    IReadOnlyCollection<SourceMapping> Mappings { get; }

    Task UpdateRepo(
        SourceMapping repo,
        string? targetRevision,
        bool noSquash,
        bool ignoreWorkingTree,
        CancellationToken cancellationToken);

    Task InitializeRepo(
        SourceMapping repo,
        string? targetRevision,
        bool ignoreWorkingTree,
        CancellationToken cancellationToken);
}

public class VmrManager : IVmrManager
{
    // Well known ID of an empty commit (can be used as a "commit zero" when diffing)
    private const string EmptyGitObject = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
    private const string HEAD = "HEAD";

    private static Signature CommitSignature => new(Constants.DarcBotName, Constants.DarcBotEmail, DateTimeOffset.Now);

    private readonly ILogger<VmrManager> _logger;
    private readonly IProcessManager _processManager;
    private readonly IRemoteFactory _remoteFactory;
    private readonly string _vmrPath;
    private readonly string _tmpPath;

    public IReadOnlyCollection<SourceMapping> Mappings { get; }

    public VmrManager(
        IProcessManager processManager,
        IRemoteFactory remoteFactory,
        ILogger<VmrManager> logger,
        IReadOnlyCollection<SourceMapping> mappings,
        string vmrPath,
        string tmpPath)
    {
        _logger = logger;
        _processManager = processManager;
        _remoteFactory = remoteFactory;
        _tmpPath = tmpPath;
        Mappings = mappings;

        if (!vmrPath.EndsWith(Path.PathSeparator + "src"))
        {
            vmrPath = Path.Join(vmrPath, "src");
        }

        _vmrPath = vmrPath;
    }

    public async Task InitializeRepo(SourceMapping mapping, string? targetRevision, bool ignoreWorkingTree, CancellationToken cancellationToken)
    {
        if (File.Exists(Path.Combine(_vmrPath, $".{mapping.Name}")))
        {
            throw new EmptySyncException($"Repository {mapping.Name} already exists");
        }

        _logger.LogInformation("Initializing {name}", mapping.Name);

        string clonePath = await CloneToTemp(mapping);
        string patchPath = GetPatchFilePath(mapping);

        cancellationToken.ThrowIfCancellationRequested();

        using var clone = new Repository(clonePath);
        var commit = GetCommit(clone, targetRevision is null || targetRevision == HEAD ? null : targetRevision);

        await CreatePatch(mapping, clonePath, EmptyGitObject, commit.Id.Sha, patchPath);
        cancellationToken.ThrowIfCancellationRequested();
        await ApplyPatch(mapping, patchPath, cleanWorkingTree: !ignoreWorkingTree);
        await TagRepo(mapping, commit.Id.Sha);

        var description = $"[{mapping.Name}] Initial commit ({ShortenId(commit.Id.Sha)}): " +
            commit.Message +
            Environment.NewLine + Environment.NewLine +
            $"Original commit: {mapping.DefaultRemote}/commit/{commit.Id.Sha}";

        // Commit but do not add files (they were added to index directly)
        cancellationToken.ThrowIfCancellationRequested();
        Commit(description, commit.Author);
    }

    public async Task UpdateRepo(SourceMapping mapping, string? targetRevision, bool noSquash, bool ignoreWorkingTree, CancellationToken cancellationToken)
    {
        var tagFile = Path.Combine(_vmrPath, $".{mapping.Name}");
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

        var clonePath = await CloneToTemp(mapping);

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

        if (currentCommit.Author.When > targetCommit.Author.When)
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
            var fromRevision = currentSha;

            while (commitsToCopy.TryPop(out LibGit2Sharp.Commit? commitToCopy))
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation("Updating {repo} from {current} to {next}..",
                    mapping.Name, ShortenId(fromRevision), ShortenId(commitToCopy.Id.Sha));

                var message = $"[{mapping.Name}] Sync {ShortenId(commitToCopy.Id.Sha)}: " +
                    commitToCopy.Message +
                    Environment.NewLine + Environment.NewLine +
                    $"Original commit: {mapping.DefaultRemote}/commit/{commitToCopy.Id.Sha}";

                await UpdateRepoToRevision(
                    mapping,
                    fromRevision,
                    commitToCopy.Sha,
                    clonePath,
                    ignoreWorkingTree,
                    message,
                    commitToCopy.Author,
                    cancellationToken);

                fromRevision = commitToCopy.Id.Sha;
            }
        }
        else
        {
            var message = new StringBuilder();

            message
                .AppendLine($"[{mapping.Name}] Sync {ShortenId(currentSha)} → {ShortenId(targetRevision)}")
                .AppendLine($"Diff: {mapping.DefaultRemote}/compare/{currentSha}..{targetRevision}")
                .AppendLine()
                .AppendLine($"From: {mapping.DefaultRemote}/commit/{currentSha}")
                .AppendLine($"To: {mapping.DefaultRemote}/commit/{targetRevision}")
                .AppendLine()
                .AppendLine("Commits:");
            
            while (commitsToCopy.TryPop(out LibGit2Sharp.Commit? commit))
            {
                message.Append($"  - {commit.MessageShort} ({mapping.DefaultRemote}/commit/{targetRevision})");
            }

            await UpdateRepoToRevision(
                mapping,
                currentSha,
                targetRevision,
                clonePath,
                ignoreWorkingTree,
                message.ToString(),
                CommitSignature,
                cancellationToken);
        }
    }

    private async Task UpdateRepoToRevision(
        SourceMapping mapping,
        string fromRevision,
        string toRevision,
        string clonePath,
        bool ignoreWorkingTree,
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
            await ApplyPatch(mapping, patchPath, cleanWorkingTree: !ignoreWorkingTree);
        }

        await TagRepo(mapping, toRevision);

        Commit(commitMessage, author);
    }

    private async Task<string> CloneToTemp(SourceMapping mapping)
    {
        var clonePath = Path.Combine(_tmpPath, mapping.Name);
        if (Directory.Exists(clonePath))
        {
            _logger.LogInformation("Clone of {repo} found, pulling new changes...", mapping.DefaultRemote);

            var result = await _processManager.ExecuteGit(clonePath, "pull");
            result.ThrowIfFailed($"Failed to pull new changes from {mapping.DefaultRemote} into {clonePath}");
            _logger.LogDebug("{output}", result.ToString());

            return Path.Combine(clonePath, ".git");
        }

        _logger.LogInformation("Cloning {repo} into {path}..", mapping.DefaultRemote, clonePath);

        var remoteRepo = await _remoteFactory.GetRemoteAsync(mapping.DefaultRemote, _logger);
        remoteRepo.Clone(mapping.DefaultRemote, mapping.DefaultRef, clonePath, checkoutSubmodules: false, null);

        return clonePath;
    }

    private async Task TagRepo(SourceMapping mapping, string commitId)
    {
        var tagFile = Path.Combine(_vmrPath, $".{mapping.Name}");
        await File.WriteAllTextAsync(tagFile, commitId);

        // Stage the tag file
        using var repository = new Repository(_processManager.FindGitRoot(_vmrPath));
        Commands.Stage(repository, Path.Combine(_vmrPath, "." + mapping.Name));
    }

    private async Task CreatePatch(SourceMapping mapping, string repoPath, string sha1, string sha2, string destPath)
    {
        _logger.LogInformation("Creating diff in {path}..", destPath);

        var args = new List<string>
        {
            "diff",
            "--patch",
            "--binary", // Include binary contents as base64
            "--output", // Store the diff in a .patch file
            destPath,
            $"{sha1}..{sha2}",
        };

        if (mapping.Include.Any() || mapping.Exclude.Any())
        {
            args.Add("--");
            args.AddRange(mapping.Include.Select(p => $":(glob){p}"));
            args.AddRange(mapping.Exclude.Select(p => $":(exclude,glob){p}"));
        }
        else
        {
            args.Add("--");
            args.Add($".");
        }

        var result = await _processManager.ExecuteGit(repoPath, args);
        result.ThrowIfFailed($"Failed to create an initial diff for {mapping.Name}");

        _logger.LogDebug("{output}", result.ToString());

        args = new List<string>
        {
            "rev-list",
            "--count",
            $"{sha1}..{sha2}",
        };

        var distance = (await _processManager.ExecuteGit(repoPath, args)).StandardOutput.Trim();

        _logger.LogInformation("Diff created at {path} - {distance} commit{s}, {size}",
            destPath, distance, distance == "1" ? string.Empty : "s", StringUtils.GetHumanReadableFileSize(destPath));
    }

    private async Task ApplyPatch(SourceMapping mapping, string patchPath, bool cleanWorkingTree)
    {
        var gitRoot = _processManager.FindGitRoot(_vmrPath);

        // We have to give git a relative path with forward slashes where to apply the patch
        var destPath = Path.Combine(_vmrPath, mapping.Name)
            .Replace(gitRoot, null)
            .Replace("\\", "/")
            [1..];

        _logger.LogInformation("Applying patch to {path}...", destPath);

        // This will help ignore some CR/LF issues (e.g. files with both endings)
        (await _processManager.ExecuteGit(gitRoot, "config", "apply.ignoreWhitespace", "change"))
            .ThrowIfFailed("Failed to set git config whitespace settings");

        Directory.CreateDirectory(destPath);

        IEnumerable<string> args = new[]
        {
            "apply",

            // Apply diff to index right away, not the working tree
            // This works around the fact that "git apply" failes with "already exists in working directory"
            // This happens only when case sensitive renames happened in the history
            // More details: https://lore.kernel.org/git/YqEiPf%2FJR%2FMEc3C%2F@camp.crustytoothpaste.net/t/
            "--cached",

            // Options to help with CR/LF and similar problems
            "--ignore-space-change",

            // Where to apply the patch into
            "--directory",
            destPath,

            patchPath,
        };

        var result = await _processManager.ExecuteGit(gitRoot, args);
        result.ThrowIfFailed($"Failed to apply the patch for {destPath}");
        _logger.LogDebug("{output}", result.ToString());

        if (cleanWorkingTree)
        {
            // After we apply the diff to the index, working tree won't have the files so they will be missing
            // We have to reset working tree to the index then
            // This will end up having the working tree all staged
            _logger.LogInformation("Resetting the working tree...");
            args = new[] { "checkout", destPath };
            result = await _processManager.ExecuteGit(gitRoot, args);
            result.ThrowIfFailed($"Failed to clean the working tree");
            _logger.LogDebug("{output}", result.ToString());
        }
    }

    private void Commit(string commitMessage, Signature author)
    {
        _logger.LogInformation("Committing..");

        var watch = Stopwatch.StartNew();
        using var repository = new Repository(_processManager.FindGitRoot(_vmrPath));
        var commit = repository.Commit(commitMessage, author, CommitSignature);

        _logger.LogInformation("Created {sha} in {duration} seconds", ShortenId(commit.Id.Sha), (int) watch.Elapsed.TotalSeconds);
    }

    private async Task<bool> HasRemoteUpdates(SourceMapping mapping, string currentSha)
    {
        var remoteRepo = await _remoteFactory.GetRemoteAsync(mapping.DefaultRemote, _logger);
        var lastCommit = await remoteRepo.GetLatestCommitAsync(mapping.DefaultRemote, mapping.DefaultRef);
        return !lastCommit.Equals(currentSha, StringComparison.InvariantCultureIgnoreCase);
    }

    private string GetPatchFilePath(SourceMapping mapping) => Path.Combine(_tmpPath, $"{mapping.Name}.patch");

    private static LibGit2Sharp.Commit GetCommit(Repository repository, string? sha)
    {
        var commit = sha is null ? repository.Commits.FirstOrDefault() : repository.Commits.FirstOrDefault(c => c.Id.Sha.StartsWith(sha));
        return commit ?? throw new InvalidOperationException($"Failed to find commit {sha} in {repository}");
    }

    private static string ShortenId(string commitSha) => commitSha[..7];
}
