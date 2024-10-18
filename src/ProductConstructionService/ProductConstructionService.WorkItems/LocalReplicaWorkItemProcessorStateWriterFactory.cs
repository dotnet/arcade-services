// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;
public class LocalReplicaWorkItemProcessorStateWriterFactory : IReplicaWorkItemProcessorStateWriterFactory
{
    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ILogger<WorkItemProcessorStateWriter> _logger;

    public LocalReplicaWorkItemProcessorStateWriterFactory(IRedisCacheFactory redisCacheFactory, ILogger<WorkItemProcessorStateWriter> logger)
    {
        _redisCacheFactory = redisCacheFactory;
        _logger = logger;
    }

    public Task<List<WorkItemProcessorStateWriter>> GetAllWorkItemProcessorStateWritersAsync()
    {
        return Task.FromResult<List<WorkItemProcessorStateWriter>>([ new WorkItemProcessorStateWriter(
            _redisCacheFactory,
            WorkItemConfiguration.LocalReplicaName,
            _logger) ]);
    }
}
