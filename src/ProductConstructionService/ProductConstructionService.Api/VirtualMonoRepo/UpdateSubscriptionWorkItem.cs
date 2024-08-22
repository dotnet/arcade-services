// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.VirtualMonoRepo;

public class UpdateSubscriptionWorkItem : WorkItem
{
    /// <summary>
    /// Subscription that is being triggered.
    /// </summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>
    /// Build that is being flown.
    /// </summary>
    public required int BuildId { get; init; }
}
