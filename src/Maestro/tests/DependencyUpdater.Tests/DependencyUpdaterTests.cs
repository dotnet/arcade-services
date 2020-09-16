// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Maestro.Contracts;
using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Data;
using Moq;
using NUnit.Framework;
using ServiceFabricMocks;

namespace DependencyUpdater.Tests
{
    public class DependencyUpdaterTests
    {
        private Lazy<BuildAssetRegistryContext> _context;
        protected Mock<IHostEnvironment> Env;
        protected ServiceProvider Provider;
        protected IServiceScope Scope;
        protected MockReliableStateManager StateManager;
        protected Mock<ISubscriptionActor> SubscriptionActor;
        protected Mock<IRemoteFactory> RemoteFactory;

        [SetUp]
        public void DependencyUpdaterTests_SetUp()
        {
            var services = new ServiceCollection();
            StateManager = new MockReliableStateManager();
            SubscriptionActor = new Mock<ISubscriptionActor>(MockBehavior.Strict);
            RemoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
            Env = new Mock<IHostEnvironment>(MockBehavior.Strict);
            services.AddSingleton(Env.Object);
            services.AddSingleton<IReliableStateManager>(StateManager);
            services.AddLogging();
            services.AddDbContext<BuildAssetRegistryContext>(
                options =>
                {
                    options.UseInMemoryDatabase("BuildAssetRegistry");
                    options.EnableServiceProviderCaching(false);
                });
            var proxyFactory = new Mock<IActorProxyFactory<ISubscriptionActor>>();
            proxyFactory.Setup(l => l.Lookup(It.IsAny<ActorId>()))
                .Returns((ActorId id) =>
                {
                    ActorId = id;
                    return SubscriptionActor.Object;
                });
            services.AddSingleton(proxyFactory.Object);
            services.AddSingleton(RemoteFactory.Object);
            services.AddOperationTracking(o => { });
            Provider = services.BuildServiceProvider();
            Scope = Provider.CreateScope();
            _context = new Lazy<BuildAssetRegistryContext>(GetContext);
        }

        protected ActorId ActorId { get; private set; }

        public BuildAssetRegistryContext Context => _context.Value;

        [TearDown]
        public void DependencyUpdaterTests_TearDown()
        {
            Env.VerifyAll();
            SubscriptionActor.VerifyAll();
            Scope.Dispose();
            Provider.Dispose();
        }

        private BuildAssetRegistryContext GetContext()
        {
            return Scope.ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
        }
    }
}
