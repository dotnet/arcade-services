// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.PullRequestUpdaters;
using Asset = ProductConstructionService.DependencyFlow.Model.Asset;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture, NonParallelizable]
internal class SubscriptionUpdaterTests : SubscriptionOrPullRequestUpdaterTests
{
    protected Dictionary<PullRequestUpdaterId, Mock<IPullRequestUpdater>> PullRequestUpdaters { get; private set; } = [];

    [SetUp]
    public void SubscriptionUpdaterTests_SetUp()
    {
        PullRequestUpdaters.Clear();
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);

        var updaterFactory = new Mock<IPullRequestUpdaterFactory>();

        updaterFactory
            .Setup(l => l.CreatePullRequestUpdater(It.IsAny<PullRequestUpdaterId>()))
            .Returns((PullRequestUpdaterId updaterId) =>
            {
                Mock<IPullRequestUpdater> mock = PullRequestUpdaters.GetOrAddValue(
                    updaterId,
                    () => new Mock<IPullRequestUpdater>());
                mock.Setup(updater => updater.Id).Returns(updaterId);
                return mock.Object;
            });

        services.AddSingleton(updaterFactory.Object);
    }

    internal async Task WhenUpdateAsyncIsCalled(Subscription forSubscription, Build andForBuild)
    {
        await Execute(
            async provider =>
            {
                var updater = ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(provider, forSubscription.Id);
                await updater.UpdateSubscriptionAsync(andForBuild.Id);
            });
    }

    private void ThenUpdateAssetsAsyncShouldHaveBeenCalled(PullRequestUpdaterId forUpdater, Build withBuild)
    {
        var updatedAssets = new List<List<Asset>>();
        PullRequestUpdaters.Should().ContainKey(forUpdater)
            .WhoseValue.Verify(
                a => a.UpdateAssetsAsync(
                    Subscription.Id,
                    SubscriptionType.Dependencies,
                    withBuild.Id,
                    false,
                    false));
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
            new BatchedPullRequestUpdaterId(Subscription.TargetRepository, Subscription.TargetBranch, Subscription.SourceEnabled),
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
        ThenUpdateAssetsAsyncShouldHaveBeenCalled(new NonBatchedPullRequestUpdaterId(Subscription.Id, Subscription.SourceEnabled), b);
    }
}
