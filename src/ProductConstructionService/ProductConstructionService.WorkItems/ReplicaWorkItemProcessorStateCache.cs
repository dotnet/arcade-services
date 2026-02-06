// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.ResourceManager.AppContainers;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common.Cache;

namespace ProductConstructionService.WorkItems;

public interface IReplicaWorkItemProcessorStateCacheFactory
{
    /// <summary>
    /// Returns a list of WorkItemProcessorStateCaches for all active Replicas
    /// </summary>
    Task<List<WorkItemProcessorStateCache>> GetAllWorkItemProcessorStateCachesAsync(string? revisionName = null);
}

public class ReplicaWorkItemProcessorStateCache : IReplicaWorkItemProcessorStateCacheFactory
{
    private ContainerAppResource _containerApp;
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ILogger<WorkItemProcessorStateCache> _logger;

    public ReplicaWorkItemProcessorStateCache(
        ContainerAppResource containerApp,
        IRedisCacheFactory redisCacheFactory,
        ILogger<WorkItemProcessorStateCache> logger)
    {
        _containerApp = containerApp;
        _redisCacheFactory = redisCacheFactory;
        _logger = logger;
    }

    public async Task<List<WorkItemProcessorStateCache>> GetAllWorkItemProcessorStateCachesAsync(string? revisionName = null)
    {
        if (string.IsNullOrEmpty(revisionName))
        {
            revisionName = _containerApp.Data.Configuration.Ingress.Traffic
                .Single(traffic => traffic.Weight == 100)
                .RevisionName;

            if (string.IsNullOrEmpty(revisionName))
            {
                throw new Exception("Current active revision has no revision name");
            }
        }

        // Always fetch the latest container app information, in case there was a deployment or something like that
        // in between calls
        _containerApp = await _containerApp.GetAsync();
        var activeRevision = await _containerApp.GetContainerAppRevisionAsync(revisionName);
        var replicas = activeRevision.Value.GetContainerAppReplicas().AsEnumerable();

        return
        [
            ..replicas.Select(replica => new WorkItemProcessorStateCache(_redisCacheFactory, replica.Data.Name, _logger))
        ];
    }
}
