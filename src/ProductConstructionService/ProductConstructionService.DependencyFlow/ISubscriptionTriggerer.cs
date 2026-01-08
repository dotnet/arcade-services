// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow;

public interface ISubscriptionTriggerer
{
    Task UpdateSubscriptionAsync(int buildId, bool force = false);

    Task<bool> AddDependencyFlowEventAsync(
        int updateBuildId,
        DependencyFlowEventType flowEvent,
        DependencyFlowEventReason reason,
        MergePolicyCheckResult policy,
        string flowType,
        string? url);
}
