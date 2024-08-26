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
    protected Dictionary<ActorId, Mock<IPullRequestActor>> PullRequestActors { get; private set; } = [];
    protected Dictionary<Guid, Mock<ISubscriptionActor>> SubscriptionActors { get; private set; } = [];

    [SetUp]
    public void SubscriptionActorTests_SetUp()
    {
        PullRequestActors = [];
        SubscriptionActors = [];
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);

        var actorFactory = new Mock<IActorFactory>();

        actorFactory.Setup(l => l.CreatePullRequestActor(It.IsAny<PullRequestActorId>()))
            .Returns((PullRequestActorId actorId) =>
            {
                Mock<IPullRequestActor> mock = PullRequestActors.GetOrAddValue(
                    actorId,
                    () => CreateMock<IPullRequestActor>());
                return mock.Object;
            });

        actorFactory.Setup(l => l.CreateSubscriptionActor(It.IsAny<Guid>()))
            .Returns((Guid subscriptionId) =>
            {
                Mock<ISubscriptionActor> mock = SubscriptionActors.GetOrAddValue(
                    subscriptionId,
                    () => CreateMock<ISubscriptionActor>());
                return mock.Object;
            });

        services.AddSingleton(actorFactory.Object);
    }

    internal async Task WhenUpdateAsyncIsCalled(Subscription forSubscription, Build andForBuild)
    {
        await Execute(
            async provider =>
            {
                var actor = CreateSubscriptionActor(provider);
                await actor.UpdateSubscriptionAsync(andForBuild.Id);
            });
    }

    private void ThenUpdateAssetsAsyncShouldHaveBeenCalled(ActorId forActor, Build withBuild)
    {
        var updatedAssets = new List<List<Asset>>();
        PullRequestActors
            .Should().ContainKey(forActor)
            .WhoseValue.Verify(
                a => a.UpdateAssetsAsync(Subscription.Id, SubscriptionType.Dependencies, withBuild.Id, SourceRepo, NewCommit, Capture.In(updatedAssets)));

        updatedAssets
            .Should().BeEquivalentTo(
            new List<List<Asset>>
            {
                withBuild.Assets
                    .Select(a => new Asset
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

    private ISubscriptionActor CreateSubscriptionActor(IServiceProvider serviceProvider)
    {
        var actorFactory = ActivatorUtilities.CreateInstance<IActorFactory>(serviceProvider);
        return actorFactory.CreateSubscriptionActor(Subscription.Id);
    }
}
