// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Data;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Octokit;
using ServiceFabricMocks;

namespace DependencyUpdateErrorProcessor.Tests
{
    public class DependencyUpdateErrorProcessorTests
    {
        private Lazy<TestBuildAssetRegistryContext> _context;
        protected Mock<IHostEnvironment> Env;
        protected ServiceProvider Provider;
        protected IServiceScope Scope;
        protected MockReliableStateManager StateManager;
        protected Mock<IGitHubClient> GithubClient;
        protected Mock<IGitHubApplicationClientFactory> GithubClientFactory;

        [SetUp]
        public void DependencyUpdateErrorProcessorTests_SetUp()
        {
            var services = new ServiceCollection();
            StateManager = new MockReliableStateManager();
            Env = new Mock<IHostEnvironment>(MockBehavior.Strict);
            GithubClient = new Mock<IGitHubClient>();
            GithubClientFactory = new Mock<IGitHubApplicationClientFactory>();
            GithubClientFactory.Setup(x => x.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(GithubClient.Object);
            services.AddSingleton(Env.Object);
            services.AddSingleton<IReliableStateManager>(StateManager);
            services.AddLogging();
            services.AddDbContext<TestBuildAssetRegistryContext>(
                options =>
                {
                    options.UseInMemoryDatabase("BuildAssetRegistry");
                    options.EnableServiceProviderCaching(false);
                });
            services.AddSingleton(GithubClientFactory.Object);
            services.Configure<DependencyUpdateErrorProcessorOptions>(
                (options) =>
                {
                    options.IsEnabled = true;
                    options.FyiHandle = "@epananth";
                    options.GithubUrl = "https://github.com/maestro-auth-test/maestro-test2";
                });
            Provider = services.BuildServiceProvider();
            Scope = Provider.CreateScope();
            _context = new Lazy<TestBuildAssetRegistryContext>(GetContext);
        }

        public TestBuildAssetRegistryContext Context => _context.Value;

        private TestBuildAssetRegistryContext GetContext()
        {
            return Scope.ServiceProvider.GetRequiredService<TestBuildAssetRegistryContext>();
        }

        [TearDown]
        public void DependencyUpdateErrorProcessorTests_TearDown()
        {
            Env.VerifyAll();
            Scope.Dispose();
            Provider.Dispose();
        }
    }
}
