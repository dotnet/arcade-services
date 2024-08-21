// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems.WorkItemDefinitions;

namespace ProductConstructionService.WorkItems.WorkItemProcessors;

public interface IWorkItemProcessor
{
    Task ProcessWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken);
}
