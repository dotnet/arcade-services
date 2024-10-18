// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;
public interface IReplicaWorkItemProcessorStateWriterFactory
{
    Task<List<WorkItemProcessorStateWriter>> GetAllWorkItemProcessorStatesAsync();
}

public class ReplicaWorkItemProcessorStateWriterFactory : IReplicaWorkItemProcessorStateWriterFactory
{
    private readonly ContainerAppResource _containerApp;
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ILogger<WorkItemProcessorStateWriter> _logger;

    private List<WorkItemProcessorStateWriter>? _states = null;

    public ReplicaWorkItemProcessorStateWriterFactory(
        ContainerAppResource containerApp,
        IRedisCacheFactory redisCacheFactory,
        ILogger<WorkItemProcessorStateWriter> logger)
    {
        _containerApp = containerApp;
        _redisCacheFactory = redisCacheFactory;
        _logger = logger;
    }

    public async Task<List<WorkItemProcessorStateWriter>> GetAllWorkItemProcessorStatesAsync()
    {
        if (_states != null)
        {
            return _states;
        }

        ContainerAppRevisionTrafficWeight activeRevisionTrafficWeight = _containerApp.Data.Configuration.Ingress.Traffic
            .Single(traffic => traffic.Weight == 100);

        if (string.IsNullOrEmpty(activeRevisionTrafficWeight.RevisionName))
        {
            throw new Exception("Currently active revision has no revision name");
        }

        var activeRevision = (await _containerApp.GetContainerAppRevisionAsync(activeRevisionTrafficWeight.RevisionName)).Value;
        _states = activeRevision.GetContainerAppReplicas()
            // Without this, VS can't distinguish between Enumerable and AsyncEnumerable in the Select bellow
            .ToEnumerable()
            .Select(replica => new WorkItemProcessorStateWriter(
                _redisCacheFactory,
                replica.Data.Name,
                _logger))
            .ToList();
        return _states;
    }
}
