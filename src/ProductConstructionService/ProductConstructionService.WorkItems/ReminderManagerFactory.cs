// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.Cache;

namespace ProductConstructionService.WorkItems;

public interface IReminderManagerFactory
{
    IReminderManager<T> CreateReminderManager<T>(string key) where T : WorkItem;
}

public class ReminderManagerFactory : IReminderManagerFactory
{
    private readonly IWorkItemProducerFactory _workItemProducerFactory;
    private readonly IRedisCacheFactory _cacheFactory;

    public ReminderManagerFactory(
        IWorkItemProducerFactory workItemProducerFactory,
        IRedisCacheFactory cacheFactory)
    {
        _workItemProducerFactory = workItemProducerFactory;
        _cacheFactory = cacheFactory;
    }

    public IReminderManager<T> CreateReminderManager<T>(string key) where T : WorkItem
    {
        key = $"{typeof(T).Name}_{key}";
        return new ReminderManager<T>(_workItemProducerFactory, _cacheFactory, key);
    }
}
