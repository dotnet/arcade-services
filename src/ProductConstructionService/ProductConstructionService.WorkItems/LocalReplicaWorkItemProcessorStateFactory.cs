// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;
public class LocalReplicaWorkItemProcessorStateFactory : IReplicaWorkItemProcessorStateFactory
{
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly IServiceProvider _serviceProvider;

    public LocalReplicaWorkItemProcessorStateFactory(IRedisCacheFactory redisCacheFactory, IServiceProvider serviceProvider)
    {
        _redisCacheFactory = redisCacheFactory;
        _serviceProvider = serviceProvider;
    }

    public Task<List<WorkItemProcessorState>> GetAllWorkItemProcessorStatesAsync()
    {
        return Task.FromResult<List<WorkItemProcessorState>>([ new WorkItemProcessorState(
            _redisCacheFactory,
            WorkItemConfiguration.LocalReplicaName,
            new AutoResetEvent(false),
            _serviceProvider.GetRequiredService<ILogger<WorkItemProcessorState>>()) ]);
    }
}
