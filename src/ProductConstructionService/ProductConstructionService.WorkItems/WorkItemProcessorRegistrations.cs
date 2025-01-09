// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.WorkItems;

internal class WorkItemProcessorRegistrations
{
    private readonly Dictionary<string, (Type WorkItem, Type Processor)> _processors = [];

    public void RegisterProcessor<TWorkItem, TProcessor>()
        where TWorkItem : WorkItem
        where TProcessor : IWorkItemProcessor
    {
        _processors.Add(typeof(TWorkItem).Name, (typeof(TWorkItem), typeof(TProcessor)));
    }

    public IReadOnlyDictionary<string, (Type WorkItem, Type Processor)> Processors => _processors;
}
