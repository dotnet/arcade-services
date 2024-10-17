// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;
public interface IReplicaWorkItemProcessorStateFactory
{
    Task<List<WorkItemProcessorState>> GetAllWorkItemProcessorStatesAsync();
}

public class ReplicaWorkItemProcessorStateFactory : IReplicaWorkItemProcessorStateFactory
{
    private readonly ContainerAppResource _containerApp;
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly IServiceProvider _serviceProvider;

    private List<WorkItemProcessorState>? _states = null;

    public ReplicaWorkItemProcessorStateFactory(ContainerAppResource containerApp, IRedisCacheFactory redisCacheFactory, IServiceProvider serviceProvider)
    {
        _containerApp = containerApp;
        _redisCacheFactory = redisCacheFactory;
        _serviceProvider = serviceProvider;


    }

    public async Task<List<WorkItemProcessorState>> GetAllWorkItemProcessorStatesAsync()
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
            .Select(replica => new WorkItemProcessorState(
                _redisCacheFactory,
                replica.Data.Name,
                new AutoResetEvent(false),
                _serviceProvider.GetRequiredService<ILogger<WorkItemProcessorState>>()))
            .ToList();
        return _states;
    }
}
