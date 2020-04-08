// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Maestro.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Data;
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
        protected readonly Mock<GitHubClient> GithubClient;

        public DependencyUpdateErrorProcessorTests()
        {
            var services = new ServiceCollection();
            StateManager = new MockReliableStateManager();
            Env = new Mock<IHostingEnvironment>(MockBehavior.Strict);
            GithubClient = new Mock<GitHubClient>(null);
            services.AddSingleton(Env.Object);
            services.AddSingleton<IReliableStateManager>(StateManager);
            services.AddLogging();
            services.AddDbContext<TestBuildAssetRegistryContext>(
                options => { options.UseInMemoryDatabase("BuildAssetRegistry"); });
            services.AddSingleton<Func<string,GitHubClient>>(
             repo =>
             {
                 return GithubClient.Object;
             });
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
            SetUp();
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

        public void SetUp()
        {
            var repositoryBranchUpdate = new RepositoryBranchUpdateHistoryEntry()
            {
                Repository = "https://github.com/maestro-auth-test/maestro-test2",
                Branch = "38",
                Method = "ProcessPendingUpdatesAsync",
                Success = false,
                Timestamp = new DateTime(2021, 04, 01)
            };

            var installationId = Context.GetInstallationId("TestValue");
            string token = "TestToken";
            Mock<GitHubAppTokenProvider> githubProvider = new Mock<GitHubAppTokenProvider>();
            githubProvider.Setup(x => x.GetAppToken()).Returns(token);
            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
                {repositoryBranchUpdate};
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
        }

    }
}
