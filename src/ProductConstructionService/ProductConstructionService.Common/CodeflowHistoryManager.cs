// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Maestro.Data.Models;

namespace ProductConstructionService.Common;

public interface ICodeflowHistoryManager
{
    void RefreshCodeflowHistory(string repo, string branch);
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
    List<CodeflowGraphCommit> IncomingCodeflows);

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

    public async void RefreshCodeflowHistory(string repo, string branch)
    {
        //0. Fetch latest commit from local git repo if exists
        //1. Fetch new parts from GitHub API
        //2. Fetch old parts from disk
        //3. Stitch them together
        //4. Write contents to cache
        await Task.CompletedTask;
    }

    public async Task<CodeflowHistoryResult> GetCodeflowHistory(Subscription subscription, bool fetchLatest)
    {
        //todo: implement this method
    }

    public async Task<CodeflowHistory?> GetCachedCodeflowHistoryAsync(Guid? subscriptionId)
    {
        string id = subscriptionId.ToString()!;

        var cache = _redisCacheFactory.Create<CodeflowHistory>(id);

        var cachedHistory = await cache.TryGetStateAsync();
        return cachedHistory;
    }


    public async Task<CodeflowHistory?> FetchLatestCodeflowHistoryAsync(Subscription subscription)
    {
        var cachedCommits = await GetCachedCodeflowHistoryAsync(subscription.Id);

        var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);

        var latestCommits = await remote.FetchNewerRepoCommitsAsync(
            subscription.TargetBranch,
            subscription.TargetBranch,
            cachedCommits?.Commits.FirstOrDefault()?.CommitSha,
            500);

        var latestCachedCodeflow = cachedCommits?.Commits.FirstOrDefault(
            x => x.IncomingCodeflows.Count > 0);

        var codeFlows = await FetchLatestIncomingCodeflows(
            subscription.TargetRepository,
            subscription.TargetBranch,
            latestCachedCodeflow,
            remote);

        return null;
    }

    private async Task<List<CodeflowGraphCommit>> FetchLatestIncomingCodeflows(
        string repo,
        string branch,
        CodeflowGraphCommit? latestCachedCommit,
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
}
