// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.WorkItems.WorkItemDefinitions;
public class BuildCoherencyInfoWorkItem : WorkItem
{
    public required int BuildId { get; init; }

    public override string Type => nameof(BuildCoherencyInfoWorkItem);
}
