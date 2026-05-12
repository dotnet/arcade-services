// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;

namespace ProductConstructionService.DependencyFlow;

public interface IPullRequestUpdater
{
    Task<bool> CheckPullRequestAsync(PullRequestCheck pullRequestCheck);

    Task<SubscriptionUpdateResult> ProcessPendingUpdatesAsync(
        SubscriptionUpdateWorkItem update,
        bool applyNewestOnly,
        bool forceUpdate,
        Build build);

    Task<SubscriptionUpdateResult> UpdateAssetsAsync(
        Guid subscriptionId,
        SubscriptionType type,
        int buildId,
        bool applyNewestOnly,
        bool forceUpdate = false);
}
