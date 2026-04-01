// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow;

public interface IPullRequestUpdaterFactory
{
    IPullRequestUpdater CreatePullRequestUpdater(PullRequestUpdaterId updaterId);

    IPullRequestChecker CreatePullRequestChecker(PullRequestUpdaterId updaterId);

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

    public IPullRequestChecker CreatePullRequestChecker(PullRequestUpdaterId updaterId)
    {
        IPullRequestTarget target = updaterId switch
        {
            BatchedPullRequestUpdaterId batchedId =>
                ActivatorUtilities.CreateInstance<BatchedPullRequestTarget>(_serviceProvider, batchedId),
            NonBatchedPullRequestUpdaterId nonBatchedId =>
                CreateNonBatchedTarget(nonBatchedId),
            _ => throw new NotImplementedException()
        };

        var stateManager = CreateStateManager(target);
        return CreateChecker(target, stateManager);
    }

    public ISubscriptionTriggerer CreateSubscriptionTrigerrer(Guid subscriptionId) =>
        ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_serviceProvider, subscriptionId);

    private IPullRequestStateManager CreateStateManager(IPullRequestTarget target) =>
        ActivatorUtilities.CreateInstance<PullRequestStateManager>(_serviceProvider, target);

    private IPullRequestChecker CreateChecker(IPullRequestTarget target, IPullRequestStateManager stateManager) =>
        ActivatorUtilities.CreateInstance<PullRequestChecker>(_serviceProvider, target, stateManager);

    private DependencyPullRequestUpdater CreateDependencyUpdater(IPullRequestTarget target)
    {
        var stateManager = CreateStateManager(target);
        var checker = CreateChecker(target, stateManager);
        return ActivatorUtilities.CreateInstance<DependencyPullRequestUpdater>(_serviceProvider, target, checker, stateManager);
    }

    private CodeFlowPullRequestUpdater CreateCodeFlowUpdater(NonBatchedPullRequestUpdaterId id)
    {
        var target = CreateNonBatchedTarget(id);
        var stateManager = CreateStateManager(target);
        var checker = CreateChecker(target, stateManager);
        return ActivatorUtilities.CreateInstance<CodeFlowPullRequestUpdater>(_serviceProvider, target, checker, stateManager);
    }

    private IPullRequestTarget CreateNonBatchedTarget(NonBatchedPullRequestUpdaterId id)
        => ActivatorUtilities.CreateInstance<NonBatchedPullRequestTarget>(_serviceProvider, id);
}
