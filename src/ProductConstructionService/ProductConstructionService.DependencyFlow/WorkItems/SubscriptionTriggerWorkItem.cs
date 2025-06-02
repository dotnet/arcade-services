// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItems;

public class SubscriptionTriggerWorkItem : WorkItem
{
    /// <summary>
    /// Subscription that is being triggered.
    /// </summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>
    /// Build that is being flown.
    /// </summary>
    public int? BuildId { get; init; }

    /// <summary>
    /// Force update even for PRs with pending or successful checks.
    /// </summary>
    public bool Force { get; init; }
}
