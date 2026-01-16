// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItems;

public class BackflowValidationWorkItem : WorkItem
{
    /// <summary>
    /// VMR commit SHA to calculate backflow status for.
    /// </summary>
    public required string VmrCommitSha { get; init; }

    /// <summary>
    /// Optional VMR build ID which resolves to a SHA.
    /// If specified, VmrCommitSha will be resolved from this build.
    /// </summary>
    public int? VmrBuildId { get; init; }
}
