// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;

namespace ProductConstructionService.DependencyFlow;

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
    List<CodeflowGraphCommit> OutgoingFlows);

public class CodeflowHistoryManager : ICodeflowHistoryManager
{
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly IRemote _remote;

    public CodeflowHistoryManager(IRedisCacheFactory cacheFactory)
    {
        _redisCacheFactory = cacheFactory;
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

    public async Task<CodeflowHistoryResult> GetCodeflowHistory(Guid? subscriptionId, bool fetchLatest)
    {
    }

    public async Task<CodeflowHistory?> GetCachedCodeflowHistoryAsync(Guid? subscriptionId)
    {

        string id = subscriptionId.ToString()!;

        var cache = _redisCacheFactory.Create<CodeflowHistory>(id);

        var cachedHistory = await cache.TryGetStateAsync();
        return cachedHistory;
    }


    public async Task<CodeflowHistory?> FetchLatestCodeflowHistoryAsync(Guid? subscriptionId)
    {

        await Task.CompletedTask;
        return null;
    }
}
