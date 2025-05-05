﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.DependencyFlow;

public interface IPullRequestUpdater
{
    Task<bool> CheckPullRequestAsync(
        PullRequestCheck pullRequestCheck);

    Task ProcessPendingUpdatesAsync(
        SubscriptionUpdateWorkItem update,
        bool forceApply,
        Build build);

    Task UpdateAssetsAsync(
        Guid subscriptionId,
        SubscriptionType type,
        int buildId,
        bool forceApply);

    PullRequestUpdaterId Id { get; }
}
