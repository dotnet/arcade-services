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
    CodeflowGraphCommit? IncomingCodeflow);

public class CodeflowHistoryManager : ICodeflowHistoryManager
{
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly IRemoteFactory _remoteFactory;

    public CodeflowHistoryManager(
        IRedisCacheFactory cacheFactory,
        IRemoteFactory remoteFactory)
    {
        _redisCacheFactory = cacheFactory;
        _remoteFactory = remoteFactory;
    }

    public async Task<CodeflowHistory?> GetCachedCodeflowHistoryAsync(Subscription subscription)
    {
        string id = subscription.Id.ToString()!;
        var cache = _redisCacheFactory.Create<CodeflowGraphCommit>(id);
        return await cache.TryGetStateAsync();
    }

    public async Task<CodeflowHistory?> GetCachedCodeflowHistoryAsync(
        Subscription subscription,
        string commitSha,
        int commitFetchCount)
    {
        // todo this method returns the codeflow history starting from commitSha. 
        // It only reads from redis and never modifies the cache
    }
    public async Task<CodeflowHistory?> FetchLatestCodeflowHistoryAsync(
        Subscription subscription,
        int commitFetchCount)
    {
        //todo acquire lock on the redis Zset here
        var cachedCommits = await GetCachedCodeflowHistoryAsync(subscription.Id);

        var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);

        latestCachedCommitSha = cachedCommits?.Commits.FirstOrDefault()?.CommitSha;

        var latestCommits = await remote.FetchNewerRepoCommitsAsync(
            subscription.TargetBranch,
            subscription.TargetBranch,
            latestCachedCommitSha,
            commitFetchCount);

        if (latestCommits.Count == commitFetchCount &&
            latestCommits.LastOrDefault()?.CommitSha != latestCachedCommitSha)
        {
            // we have a gap in the history - throw away cache because we can't form a continuous history
            cachedCommits = [];
        }
        else
        {
            latestCommits = latestCommits
                .Where(commit => commit.CommitSha != latestCachedCommitSha)
                .ToList();
        }

        var latestCachedCodeflow = cachedCommits?.Commits.FirstOrDefault(
            commit => commit.IncomingCodeflows != null);

        var codeFlows = await FetchLatestIncomingCodeflows(
            subscription.TargetRepository,
            subscription.TargetBranch,
            latestCommits,
            remote);

        foreach (var commit in latestCommits)
        {
            string? sourceCommitSha = codeflows.GetCodeflowSourceCommit(commit.CommitSha);
            commit.IncomingCodeflow = sourceCommitSha;
        }

        // todo cache fresh commits and release lock on the Zset
        await CacheCommits(latestCommits);

        return null;
    }

    private async Task<GraphCodeflows> FetchLatestIncomingCodeflows(
        string repo,
        string branch,
        List<CodeflowGraphCommit> latestCommits,
        IRemote? remote)
    {
        if (remote == null)
        {
            remote = await _remoteFactory.CreateRemoteAsync(repo);
        }

        var lastFlow = remote.GetLastIncomingCodeflow(branch, latestCachedCommit?.CommitSha);

        //todo: implement this method
        return null;
    }

    private async Task CacheCommits(List<CodeflowGraphCommit> commits)
    {
        // Cache the commits as part of the subscription's redis ZSet of CodeflowGraphCommit objects
        if (commits.Count == 0)
        {
            return;
        }
        var cache = _redisCacheFactory.Create<CodeflowGraphCommit>(subscription.Id.ToString()!);
        await cache.SetStateAsync(new CodeflowHistory(commits, codeflows));

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
