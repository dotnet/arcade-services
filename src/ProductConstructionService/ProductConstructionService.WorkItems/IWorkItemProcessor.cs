// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.WorkItems;

public interface IWorkItemProcessor<TWorkItem> where TWorkItem : WorkItem
{
    Task<bool> ProcessWorkItemAsync(TWorkItem workItem, CancellationToken cancellationToken);
}
