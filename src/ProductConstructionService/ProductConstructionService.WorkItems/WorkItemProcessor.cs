// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.WorkItems;

public interface IWorkItemProcessor
{
    Task<bool> ProcessWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken);

    Dictionary<string, object> GetLoggingContextData(WorkItem workItem);

    /// <summary>
    /// Returns the key to use for the Redis mutex in case we want to allow one processor to run per a given work item.
    /// E.g. there might be several subscription triggers arriving at once and we want to ensure that they only run one at a time.
    /// </summary>
    /// <returns>Null if lock is not required, otherwise a key to synchronize the execution by</returns>
    string? GetRedisMutexKey(WorkItem workItem);
}

public abstract class WorkItemProcessor<TWorkItem> : IWorkItemProcessor
    where TWorkItem : WorkItem
{
    public abstract Task<bool> ProcessWorkItemAsync(TWorkItem workItem, CancellationToken cancellationToken);

    protected virtual Dictionary<string, object> GetLoggingContextData(TWorkItem workItem)
    {
        return [];
    }

    protected virtual string? GetSynchronizationKey(TWorkItem workItem) => null;

    public async Task<bool> ProcessWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken)
    {
        return await ProcessWorkItemAsync(GetTypedWorkItem(workItem), cancellationToken);
    }

    public Dictionary<string, object> GetLoggingContextData(WorkItem workItem)
    {
        return GetLoggingContextData(GetTypedWorkItem(workItem));
    }

    public string? GetRedisMutexKey(WorkItem workItem)
    {
        return GetSynchronizationKey(GetTypedWorkItem(workItem));
    }

    private static TWorkItem GetTypedWorkItem(WorkItem workItem)
    {
        if (workItem is not TWorkItem typedWorkItem)
        {
            throw new NonRetriableException($"Expected work item of type {typeof(TWorkItem)}, but got {workItem.GetType()}");
        }

        return typedWorkItem;
    }
}
