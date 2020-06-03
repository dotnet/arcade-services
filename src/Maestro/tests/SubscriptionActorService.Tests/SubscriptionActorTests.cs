// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro.Contracts;
using Maestro.Data.Models;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Actors;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using Xunit;
using Asset = Maestro.Contracts.Asset;

namespace SubscriptionActorService.Tests
{
    public class SubscriptionActorTests : SubscriptionOrPullRequestActorTests
    {
        private readonly Dictionary<ActorId, Mock<IPullRequestActor>> PullRequestActors =
            new Dictionary<ActorId, Mock<IPullRequestActor>>();
        
        protected override void RegisterServices(IServiceCollection services)
        {
            var proxyFactory = new Mock<IActorProxyFactory<IPullRequestActor>>();
            proxyFactory.Setup(l => l.Lookup(It.IsAny<ActorId>()))
                .Returns((ActorId actorId) =>
                {
                    Mock<IPullRequestActor> mock = PullRequestActors.GetOrAddValue(
                        actorId,
                        CreateMock<IPullRequestActor>);
                    return mock.Object;
                });
            services.AddSingleton(proxyFactory.Object);
            base.RegisterServices(services);
        }

        internal async Task WhenUpdateAsyncIsCalled(Subscription forSubscription, Build andForBuild)
        {
            await Execute(
                async provider =>
                {
                    var actorId = new ActorId(forSubscription.Id);
                    var actor = ActivatorUtilities.CreateInstance<SubscriptionActor>(provider);
                    actor.Initialize(actorId, StateManager, Reminders);
                    await actor.UpdateAsync(andForBuild.Id);
                });
        }

        private void ThenUpdateAssetsAsyncShouldHaveBeenCalled(ActorId forActor, Build withBuild)
        {
            var updatedAssets = new List<List<Asset>>();
            PullRequestActors.Should()
                .ContainKey(forActor)
                .WhichValue.Verify(
                    a => a.UpdateAssetsAsync(Subscription.Id, withBuild.Id, SourceRepo, NewCommit, Capture.In(updatedAssets)));
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

        [Fact]
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
                PullRequestActorId.Create(Subscription.TargetRepository, Subscription.TargetBranch),
                b);
        }

        [Fact]
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
            ThenUpdateAssetsAsyncShouldHaveBeenCalled(new ActorId(Subscription.Id), b);
        }
    }
}
