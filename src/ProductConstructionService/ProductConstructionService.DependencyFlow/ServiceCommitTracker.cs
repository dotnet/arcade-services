// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Credentials;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.DependencyFlow;

internal interface IServiceCommitTracker
{
    void TrackCommit(NativePath repositoryPath, string commit);

    void ReplaceCommit(NativePath repositoryPath, string replacedCommit, string replacementCommit);

    Task<List<string>> GetReachableCommitsAsync(
        ILocalGitClient gitClient,
        NativePath repositoryPath,
        string branch,
        IEnumerable<string> previouslyTrackedCommits);
}

/// <summary>
/// Records the commits the service creates during a work item so that the ones still present on a pull request
/// branch can be persisted on the <see cref="Model.InProgressPullRequest"/>.
/// Registered as scoped, so a single instance collects the commits of one work item.
/// </summary>
internal class ServiceCommitTracker : IServiceCommitTracker
{
    private readonly Dictionary<string, List<string>> _commitsByRepository = new(StringComparer.OrdinalIgnoreCase);

    public void TrackCommit(NativePath repositoryPath, string commit)
    {
        List<string> commits = GetCommits(repositoryPath);
        if (!commits.Contains(commit, StringComparer.OrdinalIgnoreCase))
        {
            commits.Add(commit);
        }
    }

    public void ReplaceCommit(NativePath repositoryPath, string replacedCommit, string replacementCommit)
    {
        GetCommits(repositoryPath).RemoveAll(commit =>
            commit.Equals(replacedCommit, StringComparison.OrdinalIgnoreCase));
        TrackCommit(repositoryPath, replacementCommit);
    }

    public async Task<List<string>> GetReachableCommitsAsync(
        ILocalGitClient gitClient,
        NativePath repositoryPath,
        string branch,
        IEnumerable<string> previouslyTrackedCommits)
    {
        IEnumerable<string> candidates = previouslyTrackedCommits
            .Concat(GetCommits(repositoryPath))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        List<string> reachableCommits = [];
        foreach (string commit in candidates)
        {
            if (await gitClient.IsAncestorCommit(repositoryPath, commit, branch))
            {
                reachableCommits.Add(commit);
            }
        }

        return reachableCommits;
    }

    private List<string> GetCommits(NativePath repositoryPath)
    {
        string path = repositoryPath.ToString();
        if (!_commitsByRepository.TryGetValue(path, out List<string>? commits))
        {
            commits = [];
            _commitsByRepository[path] = commits;
        }

        return commits;
    }
}

/// <summary>
/// A <see cref="LocalGitClient"/> that records every commit it creates in the <see cref="IServiceCommitTracker"/>.
/// Registered as the <see cref="ILocalGitClient"/> in the dependency flow scope so that commits made through an
/// <see cref="ILocalGitRepo"/> (which delegates to <see cref="ILocalGitClient"/>) are tracked without callers opting in.
/// </summary>
internal class TrackingLocalGitClient(
        IServiceCommitTracker commitTracker,
        IRemoteTokenProvider remoteTokenProvider,
        ITelemetryRecorder telemetryRecorder,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILogger<TrackingLocalGitClient> logger)
    : LocalGitClient(remoteTokenProvider, telemetryRecorder, processManager, fileSystem, logger), ILocalGitClient
{
    async Task ILocalGitClient.CommitAsync(
        string repoPath,
        string message,
        bool allowEmpty,
        (string Name, string Email)? author,
        CancellationToken cancellationToken)
    {
        await base.CommitAsync(repoPath, message, allowEmpty, author, cancellationToken);
        commitTracker.TrackCommit(new NativePath(repoPath), await GetGitCommitAsync(repoPath, cancellationToken));
    }

    async Task ILocalGitClient.CommitAmendAsync(string repoPath, CancellationToken cancellationToken)
    {
        string replacedCommit = await GetGitCommitAsync(repoPath, cancellationToken);
        await base.CommitAmendAsync(repoPath, cancellationToken);
        commitTracker.ReplaceCommit(new NativePath(repoPath), replacedCommit, await GetGitCommitAsync(repoPath, cancellationToken));
    }
}

/// <summary>
/// A <see cref="LocalLibGit2Client"/> that records every commit it creates in the <see cref="IServiceCommitTracker"/>.
/// Registered as the <see cref="ILocalLibGit2Client"/> in the dependency flow scope so that commits made directly
/// through the client (e.g. the empty PR branch commit) are tracked alongside those made through an
/// <see cref="ILocalGitRepo"/>. Kept as a libgit2 client so that libgit2-only operations (such as push) keep working.
/// </summary>
internal class TrackingLocalLibGit2Client(
        IServiceCommitTracker commitTracker,
        IRemoteTokenProvider remoteTokenProvider,
        ITelemetryRecorder telemetryRecorder,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILogger<TrackingLocalLibGit2Client> logger)
    : LocalLibGit2Client(remoteTokenProvider, telemetryRecorder, processManager, fileSystem, logger), ILocalLibGit2Client
{
    async Task ILocalGitClient.CommitAsync(
        string repoPath,
        string message,
        bool allowEmpty,
        (string Name, string Email)? author,
        CancellationToken cancellationToken)
    {
        await base.CommitAsync(repoPath, message, allowEmpty, author, cancellationToken);
        commitTracker.TrackCommit(new NativePath(repoPath), await GetGitCommitAsync(repoPath, cancellationToken));
    }

    async Task ILocalGitClient.CommitAmendAsync(string repoPath, CancellationToken cancellationToken)
    {
        string replacedCommit = await GetGitCommitAsync(repoPath, cancellationToken);
        await base.CommitAmendAsync(repoPath, cancellationToken);
        commitTracker.ReplaceCommit(new NativePath(repoPath), replacedCommit, await GetGitCommitAsync(repoPath, cancellationToken));
    }
}
