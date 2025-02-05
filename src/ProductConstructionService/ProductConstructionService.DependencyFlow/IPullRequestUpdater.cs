﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow;

public interface IPullRequestUpdater
{
    Task<bool> CheckPullRequestAsync(
        PullRequestCheck pullRequestCheck);

    Task<bool> ProcessPendingUpdatesAsync(
        SubscriptionUpdateWorkItem update,
        bool checkNextCommitToProcess);

    Task<bool> UpdateAssetsAsync(
        Guid subscriptionId,
        SubscriptionType type,
        int buildId,
        string sourceRepo,
        string sourceSha,
        List<Asset> assets,
        bool checkNextCommitToProcess);

    PullRequestUpdaterId Id { get; }
}
