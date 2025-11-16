// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Maestro.Data.Models;

namespace ProductConstructionService.Common;

public interface ICodeflowHistoryManager
{
    Task<CodeflowHistoryResult> GetCodeflowHistory(Guid? subscriptionId);
    Task<CodeflowHistory?> FetchLatestCodeflowHistoryAsync(Guid? subscriptionId);
}

public record CodeflowHistory(
    List<CodeflowGraphCommit> Commits,
    List<CodeflowRecord> Codeflows);

public record CodeflowHistoryResult(
    CodeflowHistory? ForwardFlowHistory,
    CodeflowHistory? BackflowHistory,
    bool ResultIsOutdated);

public record CodeflowRecord(
    string SourceCommitSha,
    string TargetCommitSha,
    DateTimeOffset CodeflowMergeDate);

public record CodeflowGraphCommit(
    string CommitSha,
    DateTimeOffset CommitDate,
    string Author,
    string Description,
    string? IncomingCodeflowSha);

public class CodeflowHistoryManager(
    IRemoteFactory remoteFactory,
    IConnectionMultiplexer connection) : ICodeflowHistoryManager
{
    private readonly IRemoteFactory _remoteFactory = remoteFactory;
    private readonly IConnectionMultiplexer _connection = connection;

    public async Task<CodeflowHistory?> GetCachedCodeflowHistoryAsync(string subscriptionId, int commitFetchCount)
    {
        if (commitFetchCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(commitFetchCount));
        }

        var cache = _connection.GetDatabase();

        var commitShas = await cache.SortedSetRangeByRankWithScores(
            key: subscriptionId,
            start: 0,
            stop: commitFetchCount - 1,
            order: Order.Descending())
            .Select(e => (string)e.Element)
            .ToList();

        return await cache.StringGetAsync(commitShas)
            .Select()
    }

    public async Task<CodeflowHistory?> FetchLatestCodeflowHistoryAsync(
        Subscription subscription,
        int commitFetchCount)
    {
        var cachedCommits = await GetCachedCodeflowHistoryAsync(subscription.Id);

        var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);

        latestCachedCommitSha = cachedCommits?
            .Commits
            .FirstOrDefault()?
            .CommitSha;

        var newCommits = await remote.FetchNewerRepoCommitsAsync(
            subscription.TargetBranch,
            subscription.TargetBranch,
            latestCachedCommitSha,
            commitFetchCount);

        if (newCommits.Count == commitFetchCount
            && latestCommits.LastOrDefault()?.CommitSha != latestCachedCommitSha)
        {
            // there's a gap between the new and cached commits. clear the cache and start from scratch.
            ClearCodeflowCacheAsync(subscription.Id);
        }

        newCommits.Remove(latestCachedCommitSha);

        var codeFlows = await EnrichCommitsWithCodeflowDataAsync(
            subscription.TargetRepository,
            subscription.TargetBranch,
            !string.IsNullOrEmpty(subscription.TargetDirectory),
            latestCommits,
            remote);

        await CacheCommitsAsync(latestCommits);

        return null;
    }

    private async Task<GraphCodeflows> EnrichCommitsWithCodeflowDataAsync(
        string repo,
        string branch,
        bool isForwardFlow,
        List<CodeflowGraphCommit> commits)
    {
        if (commits.Count == 0)
        {
            return [];
        }

        remote = await _remoteFactory.CreateRemoteAsync(repo);

        var lastCommitSha = commits
            .First()
            .CommitSha;

        var commitLookups = commits.ToDictionary(c => c.CommitSha, c => c);

        while (true)
        {
            var lastFlow = isForwardFlow
                ? await _remoteFactory.GetLastVmrIncomingCodeflowAsync(branch, lastCommitSha)
                : await _remoteFactory.GetLastRepoIncomingCodeflowAsync(branch, lastCommitSha);

            if (commitLookups.Contains(lastFlow.TargetCommitSha))
            {
                commitLookups[lastFlow.TargetCommitSha].IncomingCodeflowSha = lastFlow.SourceCommitSha;
                commitLookups.Remove(lastFlow.TargetCommitSha); // prevent the possibility of infinite loops
                lastCommitSha = lastFlow.TargetCommitSha;
            }
            else
            {
                break;
            }
        }
        return commits;
    }

    private async Task CacheCommitsAsync(
        string subscriptionId,
        List<CodeflowGraphCommit> commits,
        int latestCachedCommitScore)
    {
        if (commits.Count == 0)
        {
            return;
        }
        var cache = _connection.GetDatabase();

        int i = latestCachedCommitScore ?? 0;

        var sortedSetEntries = commits
            .Select(c => new SortedSetEntry(c.CommitSha, i++))
            .ToArray();

        await cache.SortedSetAddAsync(subscriptionId, sortedSetEntries);

        // todo key must either be unique to mapping, or contain last flow info for all mappings
        // ..... or not! any one single commit is relevant only to one mapping
        var commitGraphEntries = commits
            .Select(c => new KeyValuePair<string, CodeflowGraphCommit>("CodeflowGraphCommit_" + c.CommitSha, c))
            .ToArray();

        await cache.StringSetAsync(commits);

        ClearCacheTail(); // remove any elements after the 3000th or so?
    }
}

class GraphCodeflows
{
    // keys: target repo commits that have incoing codeflows
    // values: commit SHAs of those codeflows in the source repo
    public Dictionary<string, string> Codeflows { get; set; } = [];

    /// <summary>
    /// Returns the source commit of the codeflow if targetCommitSha is a target commit of a codeflow.
    /// Otherwise, return null
    /// </summary>
    public string? GetCodeflowSourceCommit(string targetCommitSha)
    {
        if (Codeflows.TryGetValue(targetCommitSha, out var sourceCommit))
        {
            return sourceCommit;
        }
        return null;
    } 
}
