// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class SubscriptionTriggerProcessor : WorkItemProcessor<SubscriptionTriggerWorkItem>
{
    public override Task<bool> ProcessWorkItemAsync(
        SubscriptionTriggerWorkItem workItem,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
