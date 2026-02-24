// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Maestro.Common.Cache;

namespace ProductConstructionService.WorkItems;
public class WorkItemProcessorStateCache
{
    public string ReplicaName { get; init; }

    private readonly IRedisCache _cache;
    private readonly ILogger<WorkItemProcessorStateCache> _logger;

    // After 60 days the replica will be inactive for sure, so we can clean the state
    private static readonly TimeSpan StateExpirationTime = TimeSpan.FromDays(60);

    public WorkItemProcessorStateCache(IRedisCacheFactory redisCacheFactory, string replicaName, ILogger<WorkItemProcessorStateCache> logger)
    {
        _cache = redisCacheFactory.Create(replicaName);
        ReplicaName = replicaName;
        _logger = logger;
    }

    public async Task<string?> GetStateAsync()
    {
        return await _cache.GetAsync();
    }

    public async Task SetStateAsync(string state)
    {
        _logger.LogInformation("Changing replica {replicaName} state to {state}", ReplicaName, state);
        await _cache.SetAsync(state, StateExpirationTime);
    }
}
