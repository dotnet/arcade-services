// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace ProductConstructionService.Common;

public interface ICodeflowHistoryManager
{
    void RefreshCodeflowHistory(string repo, string branch);
    Task<CodeflowHistory?> GetCachedCodeflowHistory(Guid subscriptionId);
}

public class CodeflowHistoryManager : ICodeflowHistoryManager
{
    private readonly IRedisCacheFactory _cacheFactory;

    public CodeflowHistoryManager(IRedisCacheFactory cacheFactory)
    {
        _cacheFactory = cacheFactory;
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

    public async Task<CodeflowHistory?> GetCachedCodeflowHistory(Guid subscriptionId)
    {
        var cache = _cacheFactory.Create<CodeflowHistory>(subscriptionId.ToString());
        var cachedHistory = await cache.TryGetStateAsync();
        return cachedHistory;
    }
}
