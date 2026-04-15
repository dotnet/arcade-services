// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Common.Cache;

namespace ProductConstructionService.WorkItems;
public class LocalReplicaWorkItemProcessorStateCacheFactory(
    IRedisCacheFactory redisCacheFactory,
    ILogger<WorkItemProcessorStateCache> logger)
    : IReplicaWorkItemProcessorStateCacheFactory
{
    public Task<List<WorkItemProcessorStateCache>> GetAllWorkItemProcessorStateCachesAsync(string? _)
    {
        return Task.FromResult<List<WorkItemProcessorStateCache>>([ new WorkItemProcessorStateCache(
            redisCacheFactory,
            WorkItemConfiguration.LocalReplicaName,
            logger) ]);
    }
}
