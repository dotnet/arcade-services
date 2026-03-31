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
        BatchedPullRequestUpdaterId batchedId => updaterId.IsCodeFlow
            ? throw new InvalidOperationException("Batched code flow pull request updaters are not supported.")
            : CreateDependencyUpdater(ActivatorUtilities.CreateInstance<BatchedSubscriptionConfiguration>(_serviceProvider, batchedId)),
        NonBatchedPullRequestUpdaterId nonBatchedId => updaterId.IsCodeFlow
            ? CreateCodeFlowUpdater(nonBatchedId)
            : CreateDependencyUpdater(CreateNonBatchedConfiguration(nonBatchedId)),
        _ => throw new NotImplementedException()
    };

    internal IPullRequestChecker CreatePullRequestChecker(PullRequestUpdaterId updaterId) => updaterId switch
    {
        BatchedPullRequestUpdaterId batchedId => CreateChecker(
            ActivatorUtilities.CreateInstance<BatchedSubscriptionConfiguration>(_serviceProvider, batchedId)),
        NonBatchedPullRequestUpdaterId nonBatchedId => CreateChecker(
            CreateNonBatchedConfiguration(nonBatchedId)),
        _ => throw new NotImplementedException()
    };

    public ISubscriptionTriggerer CreateSubscriptionTrigerrer(Guid subscriptionId)
        => ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_serviceProvider, subscriptionId);

    private IPullRequestChecker CreateChecker(ISubscriptionConfiguration configuration)
        => ActivatorUtilities.CreateInstance<PullRequestChecker>(_serviceProvider, configuration);

    private DependencyPullRequestUpdater CreateDependencyUpdater(ISubscriptionConfiguration configuration)
    {
        var checker = CreateChecker(configuration);
        return ActivatorUtilities.CreateInstance<DependencyPullRequestUpdater>(_serviceProvider, configuration, checker);
    }

    private CodeFlowPullRequestUpdater CreateCodeFlowUpdater(NonBatchedPullRequestUpdaterId id)
    {
        var configuration = CreateNonBatchedConfiguration(id);
        var checker = CreateChecker(configuration);
        return ActivatorUtilities.CreateInstance<CodeFlowPullRequestUpdater>(_serviceProvider, configuration, checker);
    }

    private NonBatchedSubscriptionConfiguration CreateNonBatchedConfiguration(NonBatchedPullRequestUpdaterId id)
        => ActivatorUtilities.CreateInstance<NonBatchedSubscriptionConfiguration>(_serviceProvider, id);
}
