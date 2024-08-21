// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.WorkItems.WorkItemDefinitions;

public class SubscriptionRetryWorkItem : WorkItem
{
    public required Guid SubscriptionId { get; init; }

    public required string Method { get; init; }

    public required string Arguments { get; init; }

    public override string Type => nameof(SubscriptionRetryWorkItem);
}
