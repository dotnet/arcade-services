// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.WorkItems;

public interface IWorkItemProcessor
{
    Task<bool> ProcessWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken);

    Dictionary<string, object> GetLoggingContextData(WorkItem workItem);
}

public abstract class WorkItemProcessor<TWorkItem> : IWorkItemProcessor
    where TWorkItem : WorkItem
{
    public abstract Task<bool> ProcessWorkItemAsync(TWorkItem workItem, CancellationToken cancellationToken);

    protected virtual Dictionary<string, object> GetLoggingContextData(TWorkItem workItem)
    {
        return [];
    }

    public async Task<bool> ProcessWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken)
    {
        if (workItem is not TWorkItem typedWorkItem)
        {
            throw new NonRetriableException($"Expected work item of type {typeof(TWorkItem)}, but got {workItem.GetType()}");
        }

        return await ProcessWorkItemAsync(typedWorkItem, cancellationToken);
    }

    public Dictionary<string, object> GetLoggingContextData(WorkItem workItem)
    {
        if (workItem is not TWorkItem typedWorkItem)
        {
            throw new NonRetriableException($"Expected work item of type {typeof(TWorkItem)}, but got {workItem.GetType()}");
        }

        return GetLoggingContextData(typedWorkItem);
    }
}
