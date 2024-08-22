// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.WorkItems;

public interface IWorkItemProcessor
{
    Task<bool> ProcessWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken);
}
public interface IWorkItemProcessor<TWorkItem> : IWorkItemProcessor where TWorkItem : WorkItem
{
    Task<bool> ProcessWorkItemAsync(TWorkItem workItem, CancellationToken cancellationToken);
}
