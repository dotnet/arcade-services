// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.WorkItems.WorkItemDefinitions;
public class PullRequestRetryWorkItem : WorkItem
{
    /// <summary>
    /// Repository url
    /// </summary>
    public required string Repository { get; init; }

    /// <summary>
    /// Branch name
    /// </summary>
    public required string Branch { get; init; }

    /// <summary>
    /// Method name that will be invoked by the WorkItem processor
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Method arguments that will be passed to the Method
    /// </summary>
    public required string Arguments { get; init; }

    public override string Type => nameof(PullRequestRetryWorkItem);
}
