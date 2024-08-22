// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItems;
public class BuildCoherencyInfoWorkItem : WorkItem
{
    public required int BuildId { get; init; }
}
