// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

#nullable disable
namespace ProductConstructionService.DependencyFlow.WorkItems;

public abstract class DependencyFlowWorkItem : WorkItem
{
    public required string UpdaterId { get; init; }
}
