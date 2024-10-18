// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;
public class WorkItemProcessorStateWriter
{
    public string ReplicaName { get; init; }

    private readonly IRedisCache _cache;
    private readonly ILogger<WorkItemProcessorStateWriter> _logger;

    private static TimeSpan StateExpirationTime = TimeSpan.FromDays(30);

    public WorkItemProcessorStateWriter(IRedisCacheFactory redisCacheFactory, string replicaName, ILogger<WorkItemProcessorStateWriter> logger)
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
