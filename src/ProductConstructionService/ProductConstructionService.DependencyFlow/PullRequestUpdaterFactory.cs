// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;

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
        BatchedPullRequestUpdaterId batchedId => updaterId.IsCodeFlow
            ? throw new InvalidOperationException("Batched code flow pull request updaters are not supported.")
            : CreateDependencyUpdater(ActivatorUtilities.CreateInstance<BatchedPullRequestTarget>(_serviceProvider, batchedId)),
        NonBatchedPullRequestUpdaterId nonBatchedId => updaterId.IsCodeFlow
            ? CreateCodeFlowUpdater(nonBatchedId)
            : CreateDependencyUpdater(CreateNonBatchedTarget(nonBatchedId)),
        _ => throw new NotImplementedException()
    };

    public ISubscriptionTriggerer CreateSubscriptionTrigerrer(Guid subscriptionId) =>
        ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_serviceProvider, subscriptionId);

    private IPullRequestStateManager CreateStateManager(IPullRequestTarget target) =>
        ActivatorUtilities.CreateInstance<PullRequestStateManager>(_serviceProvider, target);

    private DependencyPullRequestUpdater CreateDependencyUpdater(IPullRequestTarget target)
    {
        var stateManager = CreateStateManager(target);
        return ActivatorUtilities.CreateInstance<DependencyPullRequestUpdater>(_serviceProvider, target, stateManager);
    }

    private CodeFlowPullRequestUpdater CreateCodeFlowUpdater(NonBatchedPullRequestUpdaterId id)
    {
        var target = CreateNonBatchedTarget(id);
        var stateManager = CreateStateManager(target);
        return ActivatorUtilities.CreateInstance<CodeFlowPullRequestUpdater>(_serviceProvider, target, stateManager);
    }

    private IPullRequestTarget CreateNonBatchedTarget(NonBatchedPullRequestUpdaterId id)
        => ActivatorUtilities.CreateInstance<NonBatchedPullRequestTarget>(_serviceProvider, id);
}
