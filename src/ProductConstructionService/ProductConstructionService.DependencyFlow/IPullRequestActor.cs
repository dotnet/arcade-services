// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow;

public interface IPullRequestActor
{
    Task<(InProgressPullRequest? pr, bool canUpdate)> SynchronizeInProgressPullRequestAsync(
        InProgressPullRequest pullRequestCheck);

    Task<bool> ProcessPendingUpdatesAsync(
        SubscriptionUpdateWorkItem update);

    Task<bool> UpdateAssetsAsync(
        Guid subscriptionId,
        SubscriptionType type,
        int buildId,
        string sourceRepo,
        string sourceSha,
        List<Maestro.Contracts.Asset> assets);
}
