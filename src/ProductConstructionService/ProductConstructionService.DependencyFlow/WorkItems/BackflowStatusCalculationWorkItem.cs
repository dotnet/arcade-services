// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItems;

public class BackflowStatusCalculationWorkItem : WorkItem
{
    /// <summary>
    /// VMR build ID which will be resolved to a commit SHA.
    /// </summary>
    public required int VmrBuildId { get; init; }
}
