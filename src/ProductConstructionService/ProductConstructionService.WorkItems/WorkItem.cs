// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.WorkItems;

public abstract class WorkItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type => GetType().Name;
}
