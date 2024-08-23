// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public class PullRequestCheckProcessor : WorkItemProcessor<PullRequestCheckWorkItem>
{
    public override Task<bool> ProcessWorkItemAsync(
        PullRequestCheckWorkItem workItem,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
