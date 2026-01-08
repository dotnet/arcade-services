// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow;

public interface IPullRequestUpdaterFactory
{
    IPullRequestUpdater CreatePullRequestUpdater(PullRequestUpdaterId updaterId);

    ISubscriptionTriggerer CreateSubscriptionTrigerrer(Guid subscriptionId);
}

internal class PullRequestUpdaterFactory : IPullRequestUpdaterFactory
{
    private readonly IServiceProvider _serviceProvider;

    public PullRequestUpdaterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IPullRequestUpdater CreatePullRequestUpdater(PullRequestUpdaterId updaterId) => updaterId switch
    {
        BatchedPullRequestUpdaterId batched => ActivatorUtilities.CreateInstance<BatchedDependencyPullRequestUpdater>(_serviceProvider, batched),
        NonBatchedPullRequestUpdaterId nonBatched => ActivatorUtilities.CreateInstance<NonBatchedDependencyPullRequestUpdater>(_serviceProvider, nonBatched),
        _ => throw new NotImplementedException()
    };

    public ISubscriptionTriggerer CreateSubscriptionTrigerrer(Guid subscriptionId)
    {
        return ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_serviceProvider, subscriptionId);
    }
}
