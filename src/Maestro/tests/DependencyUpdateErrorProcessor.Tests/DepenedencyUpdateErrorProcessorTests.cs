// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Data;
using Microsoft.DotNet.GitHub.Authentication;
using Moq;
using Octokit;
using ServiceFabricMocks;

namespace DependencyUpdateErrorProcessor.Tests
{
    public class DependencyUpdateErrorProcessorTests : IDisposable
    {
        private readonly Lazy<TestBuildAssetRegistryContext> _context;
        protected readonly Mock<IHostingEnvironment> Env;
        protected readonly ServiceProvider Provider;
        protected readonly IServiceScope Scope;
        protected readonly MockReliableStateManager StateManager;
        protected readonly Mock<IGitHubClient> GithubClient;
        protected readonly Mock<IGitHubApplicationClientFactory> GithubClientFactory;

        public DependencyUpdateErrorProcessorTests()
        {
            var services = new ServiceCollection();
            StateManager = new MockReliableStateManager();
            Env = new Mock<IHostingEnvironment>(MockBehavior.Strict);
            GithubClient = new Mock<IGitHubClient>();
            GithubClientFactory = new Mock<IGitHubApplicationClientFactory>();
            GithubClientFactory.Setup(x => x.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(GithubClient.Object);
            services.AddSingleton(Env.Object);
            services.AddSingleton<IReliableStateManager>(StateManager);
            services.AddLogging();
            services.AddDbContext<TestBuildAssetRegistryContext>(
                options => { options.UseInMemoryDatabase("BuildAssetRegistry"); });
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

        public void Dispose()
        {
            Env.VerifyAll();
            Scope.Dispose();
            Provider.Dispose();
        }
    }
}
