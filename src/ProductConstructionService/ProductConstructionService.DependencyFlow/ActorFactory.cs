// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace ProductConstructionService.DependencyFlow;

public interface IActorFactory
{
    IPullRequestActor CreatePullRequestActor(PullRequestActorId actorId);

    IPullRequestActor CreatePullRequestActor(string repository, string branch);

    IPullRequestActor CreatePullRequestActor(Guid subscriptionId);

    ISubscriptionActor CreateSubscriptionActor(Guid subscriptionId);
}

internal class ActorFactory : IActorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ActorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IPullRequestActor CreatePullRequestActor(PullRequestActorId actorId) => actorId switch
    {
        BatchedPullRequestActorId batched => ActivatorUtilities.CreateInstance<BatchedPullRequestActor>(_serviceProvider, batched),
        NonBatchedPullRequestActorId nonBatched => ActivatorUtilities.CreateInstance<NonBatchedPullRequestActor>(_serviceProvider, nonBatched),
        _ => throw new NotImplementedException()
    };

    public IPullRequestActor CreatePullRequestActor(string repository, string branch)
    {
        return ActivatorUtilities.CreateInstance<BatchedPullRequestActor>(_serviceProvider, new BatchedPullRequestActorId(repository, branch));
    }

    public IPullRequestActor CreatePullRequestActor(Guid subscriptionId)
    {
        return ActivatorUtilities.CreateInstance<NonBatchedPullRequestActor>(_serviceProvider, new NonBatchedPullRequestActorId(subscriptionId));
    }

    public ISubscriptionActor CreateSubscriptionActor(Guid subscriptionId)
    {
        return ActivatorUtilities.CreateInstance<SubscriptionActor>(_serviceProvider, subscriptionId);
    }
}
