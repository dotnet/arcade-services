// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Pipelines.Sockets.Unofficial.Arenas;
using StackExchange.Redis;

namespace ProductConstructionService.Common;

public interface ICodeflowHistoryManager
{
    Task<IEnumerable<CodeflowGraphCommit>> GetCachedCodeflowHistoryAsync(
        string subscriptionId,
        int commitFetchCount);

    Task<IEnumerable<CodeflowGraphCommit>> FetchLatestCodeflowHistoryAsync(
        Subscription subscription,
        int commitFetchCount);
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
    private const int MaxCommitsCached = 3000;

    public async Task<IEnumerable<CodeflowGraphCommit>> GetCachedCodeflowHistoryAsync(
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

    public async Task<IEnumerable<CodeflowGraphCommit>> FetchLatestCodeflowHistoryAsync(
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
            newCommits.RemoveLast();
        }
        else
        {
            // the fetched commits do not connect to the cached commits
            // we don't know how many commits are missing, so clear the cache and start over
            await ClearCodeflowCacheAsync(subscription.Id.ToString());
        }

        var graphCommits = await EnrichCommitsWithCodeflowDataAsync(
            newCommits,
            subscription.TargetRepository,
            subscription.TargetBranch,
            !string.IsNullOrEmpty(subscription.TargetDirectory));

        await CacheCommitsAsync(subscription.Id.ToString(), graphCommits);

        return [.. graphCommits
            .Concat(cachedCommits)
            .Take(commitFetchCount)];
    }

    private async Task<LinkedList<CodeflowGraphCommit>> EnrichCommitsWithCodeflowDataAsync(
        LinkedList<CodeflowGraphCommit> commits,
        string repo,
        string branch,
        bool isForwardFlow)
    {
        var remote = await _remoteFactory.CreateRemoteAsync(repo);

        var current = commits.First;

        while (current != null)
        {
            Codeflow lastFlow = isForwardFlow
                ? await remote.GetLastIncomingForwardFlowAsync(branch, current.Value.CommitSha)
                : await remote.GetLastIncomingBackflowAsync(branch, current.Value.CommitSha);

            var target = current;

            while (target != null && target.Value.CommitSha != lastFlow.TargetSha)
                target = target.Next;

            if (target == null)
                break;

            target.Value = target.Value with
            {
                IncomingCodeflowSha = lastFlow.SourceSha,
            };

            current = target.Next;
        }
        return commits;
    }

    private async Task CacheCommitsAsync(
        string subscriptionId,
        IEnumerable<CodeflowGraphCommit> commits,
        int latestCachedCommitScore = 0)
    {
        if (!commits.Any())
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

        if (latestCachedCommitScore > MaxCommitsCached)
        {
            await cache.SortedSetRemoveRangeByScoreAsync(
                key: subscriptionId,
                start: 0,
                stop: latestCachedCommitScore - MaxCommitsCached);
        }
    }

    private async Task ClearCodeflowCacheAsync(string subscriptionId)
    {
        var cache = _connection.GetDatabase();
        await cache.KeyDeleteAsync(GetSortedSetKey(subscriptionId));
    }

    private async Task<LinkedList<CodeflowGraphCommit>> FetchNewCommits(
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

        return new LinkedList<CodeflowGraphCommit>(
            newCommits.Select(commit => new CodeflowGraphCommit(
                CommitSha: commit.Sha,
                Author: commit.Author,
                Description: commit.Message,
                IncomingCodeflowSha: null,
                redisScore: null)));
    }
}
