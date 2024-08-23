// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using NUnit.Framework;
using Asset = Maestro.Contracts.Asset;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture, NonParallelizable]
internal class SubscriptionActorTests : SubscriptionOrPullRequestActorTests
{
    private Dictionary<ActorId, Mock<IPullRequestActor>> _pullRequestActors = null!;
    private Dictionary<Guid, Mock<ISubscriptionActor>> _subscriptionActors = null!;

    [SetUp]
    public void SubscriptionActorTests_SetUp()
    {
        _pullRequestActors = [];
        _subscriptionActors = [];
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        var actorFactory = new Mock<IActorFactory>();

        actorFactory.Setup(l => l.CreatePullRequestActor(It.IsAny<PullRequestActorId>()))
            .Returns((ActorId actorId) =>
            {
                Mock<IPullRequestActor> mock = _pullRequestActors.GetOrAddValue(
                    actorId,
                    () => CreateMock<IPullRequestActor>());
                return mock.Object;
            });

        actorFactory.Setup(l => l.CreateSubscriptionActor(It.IsAny<Guid>()))
            .Returns((Guid subscriptionId) =>
            {
                Mock<ISubscriptionActor> mock = _subscriptionActors.GetOrAddValue(
                    subscriptionId,
                    () => CreateMock<ISubscriptionActor>());
                return mock.Object;
            });

        services.AddSingleton(actorFactory.Object);

        base.RegisterServices(services);
    }

    internal async Task WhenUpdateAsyncIsCalled(Subscription forSubscription, Build andForBuild)
    {
        await Execute(
            async provider =>
            {
                var actor = ActivatorUtilities.CreateInstance<SubscriptionActor>(provider);
                await actor.UpdateSubscriptionAsync(andForBuild.Id);
            });
    }

    private void ThenUpdateAssetsAsyncShouldHaveBeenCalled(ActorId forActor, Build withBuild)
    {
        var updatedAssets = new List<List<Asset>>();
        _pullRequestActors.Should()
            .ContainKey(forActor)
            .WhoseValue.Verify(
                a => a.UpdateAssetsAsync(Subscription.Id, SubscriptionType.Dependencies, withBuild.Id, SourceRepo, NewCommit, Capture.In(updatedAssets)));
        updatedAssets.Should()
            .BeEquivalentTo(
                new List<List<Asset>>
                {
                    withBuild.Assets.Select(
                            a => new Asset
                            {
                                Name = a.Name,
                                Version = a.Version
                            })
                        .ToList()
                });
    }

    [Test]
    public async Task BatchableEveryBuildSubscription()
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = true,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b = GivenANewBuild(true);

        await WhenUpdateAsyncIsCalled(Subscription, b);
        ThenUpdateAssetsAsyncShouldHaveBeenCalled(
            new BatchedPullRequestActorId(Subscription.TargetRepository, Subscription.TargetBranch),
            b);
    }

    [Test]
    public async Task NotBatchableEveryBuildSubscription()
    {
        GivenATestChannel();
        GivenASubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild
            });
        Build b = GivenANewBuild(true);

        await WhenUpdateAsyncIsCalled(Subscription, b);
        ThenUpdateAssetsAsyncShouldHaveBeenCalled(new NonBatchedPullRequestActorId(Subscription.Id), b);
    }
}
