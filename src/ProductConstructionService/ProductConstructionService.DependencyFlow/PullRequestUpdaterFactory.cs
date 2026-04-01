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
            : CreateDependencyUpdater(ActivatorUtilities.CreateInstance<BatchedSubscriptionConfiguration>(_serviceProvider, batchedId)),
        NonBatchedPullRequestUpdaterId nonBatchedId => updaterId.IsCodeFlow
            ? CreateCodeFlowUpdater(nonBatchedId)
            : CreateDependencyUpdater(CreateNonBatchedConfiguration(nonBatchedId)),
        _ => throw new NotImplementedException()
    };

    public IPullRequestChecker CreatePullRequestChecker(PullRequestUpdaterId updaterId)
    {
        ISubscriptionConfiguration configuration = updaterId switch
        {
            BatchedPullRequestUpdaterId batchedId =>
                ActivatorUtilities.CreateInstance<BatchedSubscriptionConfiguration>(_serviceProvider, batchedId),
            NonBatchedPullRequestUpdaterId nonBatchedId =>
                CreateNonBatchedConfiguration(nonBatchedId),
            _ => throw new NotImplementedException()
        };

        var stateManager = CreateStateManager(configuration);
        return CreateChecker(configuration, stateManager);
    }

    public ISubscriptionTriggerer CreateSubscriptionTrigerrer(Guid subscriptionId) =>
        ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_serviceProvider, subscriptionId);

    private IPullRequestStateManager CreateStateManager(ISubscriptionConfiguration configuration) =>
        ActivatorUtilities.CreateInstance<PullRequestStateManager>(_serviceProvider, configuration);

    private IPullRequestChecker CreateChecker(ISubscriptionConfiguration configuration, IPullRequestStateManager stateManager) =>
        ActivatorUtilities.CreateInstance<PullRequestChecker>(_serviceProvider, configuration, stateManager);

    private DependencyPullRequestUpdater CreateDependencyUpdater(ISubscriptionConfiguration configuration)
    {
        var stateManager = CreateStateManager(configuration);
        var checker = CreateChecker(configuration, stateManager);
        return ActivatorUtilities.CreateInstance<DependencyPullRequestUpdater>(_serviceProvider, configuration, checker, stateManager);
    }

    private CodeFlowPullRequestUpdater CreateCodeFlowUpdater(NonBatchedPullRequestUpdaterId id)
    {
        var configuration = CreateNonBatchedConfiguration(id);
        var stateManager = CreateStateManager(configuration);
        var checker = CreateChecker(configuration, stateManager);
        return ActivatorUtilities.CreateInstance<CodeFlowPullRequestUpdater>(_serviceProvider, configuration, checker, stateManager);
    }

    private ISubscriptionConfiguration CreateNonBatchedConfiguration(NonBatchedPullRequestUpdaterId id)
        => ActivatorUtilities.CreateInstance<NonBatchedSubscriptionConfiguration>(_serviceProvider, id);
}
