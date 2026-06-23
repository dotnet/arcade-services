// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.DependencyFlow.WorkItems;

internal class CodeflowUpdateCheck : DependencyFlowWorkItem
{
    public required Guid SubscriptionId { get; init; }
    public required string PreviousSourceSha { get; init; }
    public required string CurrentSourceSha { get; init; }
}
