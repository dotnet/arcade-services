// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;
public interface IReplicaWorkItemProcessorStateCacheFactory
{
    /// <summary>
    /// Returns a list of WorkItemProcessorStateCaches for all active Replicas
    /// </summary>
    Task<List<WorkItemProcessorStateCache>> GetAllWorkItemProcessorStateCachesAsync();
}

public class ReplicaWorkItemProcessorStateCache : IReplicaWorkItemProcessorStateCacheFactory
{
    private readonly ContainerAppResource _containerApp;
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

    public async Task<List<WorkItemProcessorStateCache>> GetAllWorkItemProcessorStateCachesAsync()
    {
        ContainerAppRevisionTrafficWeight activeRevisionTrafficWeight = _containerApp.Data.Configuration.Ingress.Traffic
            .Single(traffic => traffic.Weight == 100);

        if (string.IsNullOrEmpty(activeRevisionTrafficWeight.RevisionName))
        {
            throw new Exception("Current active revision has no revision name");
        }

        var activeRevision = (await _containerApp.GetContainerAppRevisionAsync(activeRevisionTrafficWeight.RevisionName)).Value;
        return activeRevision.GetContainerAppReplicas()
            // Without this, VS can't distinguish between Enumerable and AsyncEnumerable in the Select bellow
            .ToEnumerable()
            .Select(replica => new WorkItemProcessorStateCache(
                _redisCacheFactory,
                replica.Data.Name,
                _logger))
            .ToList();
    }
}
