// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using StackExchange.Redis;

namespace ProductConstructionService.Common;

public interface ICodeflowHistoryManager
{
    Task<List<CodeflowGraphCommit>> GetCachedCodeflowHistoryAsync(string subscriptionId, int commitFetchCount);
    Task<List<CodeflowGraphCommit>> FetchLatestCodeflowHistoryAsync(Subscription subscription, int commitFetchCount);
}

public record CodeflowGraphCommit(
    string CommitSha,
    string Author,
    string Description,
    string? IncomingCodeflowSha,
    int? redisScore);

public class CodeflowHistoryManager(
    IRemoteFactory remoteFactory,
    IConnectionMultiplexer connection) : ICodeflowHistoryManager
{
    private readonly IRemoteFactory _remoteFactory = remoteFactory;
    private readonly IConnectionMultiplexer _connection = connection;

    private static RedisKey GetCodeflowGraphCommitKey(string id) => $"CodeflowGraphCommit_{id}";
    private static RedisKey GetSortedSetKey(string id) => $"CodeflowHistory_{id}";

    private const int MaxCommitFetchCount = 500;


    public async Task<List<CodeflowGraphCommit>> GetCachedCodeflowHistoryAsync(
        string subscriptionId,
        int commitFetchCount = 100)
    {
        if (commitFetchCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(commitFetchCount));
        }

        var cache = _connection.GetDatabase();

        var res = await cache.SortedSetRangeByRankWithScoresAsync(
            key: subscriptionId,
            start: 0,
            stop: commitFetchCount - 1,
            order: Order.Descending);

        var commitKeys = res
            .Select(e => new RedisKey(e.Element.ToString()))
            .ToArray();

        var commitValues = await cache.StringGetAsync(commitKeys);

        if (commitValues.Any(val => !val.HasValue))
        {
            throw new InvalidOperationException($"Corrupted commit data encountered.");
        }

        return [.. commitValues
            .Select(commit => JsonSerializer.Deserialize<CodeflowGraphCommit>(commit.ToString()))
            .OfType<CodeflowGraphCommit>()];
    }

    public async Task<List<CodeflowGraphCommit>> FetchLatestCodeflowHistoryAsync(
        Subscription subscription,
        int commitFetchCount = 100)
    {
        var cachedCommits = await GetCachedCodeflowHistoryAsync(subscription.Id.ToString());

        var latestCachedCommit = cachedCommits.FirstOrDefault();

        var newCommits = await FetchNewCommits(
            subscription.TargetRepository,
            subscription.TargetBranch,
            latestCachedCommit?.CommitSha);

        if (newCommits.LastOrDefault()?.CommitSha == latestCachedCommit?.CommitSha)
        {
            newCommits.RemoveAt(newCommits.Count - 1);
        }
        else
        {
            // there's a gap between the new and cached commits. clear the cache and start from scratch.
            await ClearCodeflowCacheAsync(subscription.Id.ToString());
        }

        var graphCommits = await EnrichCommitsWithCodeflowDataAsync(
            subscription.TargetRepository,
            subscription.TargetBranch,
            !string.IsNullOrEmpty(subscription.TargetDirectory),
            newCommits);

        await CacheCommitsAsync(subscription.Id.ToString(), graphCommits);

        return [.. graphCommits
            .Concat(cachedCommits)
            .Take(commitFetchCount)];
    }

    private async Task<List<CodeflowGraphCommit>> EnrichCommitsWithCodeflowDataAsync(
        string repo,
        string branch,
        bool isForwardFlow,
        List<CodeflowGraphCommit> commits)
    {
        if (commits.Count == 0)
        {
            return [];
        }

        var remote = await _remoteFactory.CreateRemoteAsync(repo);

        var lastCommitSha = commits.First().CommitSha;

        var commitLookups = commits.ToDictionary(c => c.CommitSha, c => c);

        while (true)
        {
            var lastFlow = isForwardFlow
                ? await remote.GetLastVmrIncomingCodeflowAsync(branch, lastCommitSha)
                : await remote.GetLastRepoIncomingCodeflowAsync(branch, lastCommitSha);

            if (!commitLookups.Contains(lastFlow.TargetCommitSha))
            {
                // there are no more incoming codeflows within the commit range
                break;
            }

            commitLookups[lastFlow.TargetCommitSha].IncomingCodeflowSha = lastFlow.SourceCommitSha;
            commitLookups.Remove(lastFlow.TargetCommitSha);
            lastCommitSha = lastFlow.TargetCommitSha;
        }

        return commits;
    }

    private async Task CacheCommitsAsync(
        string subscriptionId,
        List<CodeflowGraphCommit> commits,
        int latestCachedCommitScore = 0)
    {
        if (commits.Count == 0)
        {
            return;
        }

        var cache = _connection.GetDatabase();

        var sortedSetEntries = commits
            .Select(c => new SortedSetEntry(c.CommitSha, latestCachedCommitScore++))
            .ToArray();

        await cache.SortedSetAddAsync(subscriptionId, sortedSetEntries);

        var commitGraphEntries = commits
            .Select(c => new KeyValuePair<RedisKey, RedisValue>(
                GetCodeflowGraphCommitKey(c.CommitSha),
                JsonSerializer.Serialize(c)))
            .ToArray();

        await cache.StringSetAsync(commitGraphEntries);

        //todo remove any elements after the 3000th or so? to keep the cache from growing indefinitely
    }

    private async Task ClearCodeflowCacheAsync(string subscriptionId)
    {
        var cache = _connection.GetDatabase();
        await cache.KeyDeleteAsync(GetSortedSetKey(subscriptionId));
    }

    private async Task<List<CodeflowGraphCommit>> FetchNewCommits(
        string targetRepository,
        string targetBranch,
        string? latestCachedCommitSha)
    {
        var remote = await _remoteFactory.CreateRemoteAsync(targetRepository);

        var newCommits = await remote.FetchNewerRepoCommitsAsync(
            targetRepository,
            targetBranch,
            latestCachedCommitSha,
            MaxCommitFetchCount);

        return [.. newCommits
            .Select(commit => new CodeflowGraphCommit(
                CommitSha: commit.Sha,
                Author: commit.Author,
                Description: commit.Message,
                IncomingCodeflowSha: null,
                redisScore: null))];
    }
}
