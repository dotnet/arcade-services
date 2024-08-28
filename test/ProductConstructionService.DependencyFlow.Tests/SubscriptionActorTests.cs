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
    protected Dictionary<ActorId, Mock<IPullRequestUpdater>> PullRequestActors { get; private set; } = [];

    [SetUp]
    public void SubscriptionActorTests_SetUp()
    {
        PullRequestActors.Clear();
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);

        var updaterFactory = new Mock<IPullRequestUpdaterFactory>();

        updaterFactory
            .Setup(l => l.CreatePullRequestUpdater(It.IsAny<PullRequestUpdaterId>()))
            .Returns((PullRequestUpdaterId updaterId) =>
            {
                Mock<IPullRequestUpdater> mock = PullRequestActors.GetOrAddValue(
                    updaterId,
                    () => CreateMock<IPullRequestUpdater>());
                return mock.Object;
            });

        services.AddSingleton(updaterFactory.Object);
    }

    internal async Task WhenUpdateAsyncIsCalled(Subscription forSubscription, Build andForBuild)
    {
        await Execute(
            async provider =>
            {
                ISubscriptionTriggerer actor = ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(provider, forSubscription.Id);
                await actor.UpdateSubscriptionAsync(andForBuild.Id);
            });
    }

    private void ThenUpdateAssetsAsyncShouldHaveBeenCalled(ActorId forActor, Build withBuild)
    {
        var updatedAssets = new List<List<Asset>>();
        PullRequestActors.Should().ContainKey(forActor)
            .WhoseValue.Verify(
                a => a.UpdateAssetsAsync(Subscription.Id, SubscriptionType.Dependencies, withBuild.Id, SourceRepo, NewCommit, Capture.In(updatedAssets)));

        updatedAssets.Should().BeEquivalentTo(
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
        ThenUpdateAssetsAsyncShouldHaveBeenCalled(new NonBatchedPullRequestUpdaterId(Subscription.Id), b);
    }
}
