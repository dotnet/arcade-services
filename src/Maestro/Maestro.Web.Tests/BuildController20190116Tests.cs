using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Web.Api.v2019_01_16.Controllers;
using Maestro.Web.Api.v2019_01_16.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Maestro.Web.Tests
{
    [Collection(nameof(DatabaseCollection))]
    public class BuildController20190116Tests
    {
        private readonly ITestOutputHelper _output;
        private readonly TestDatabaseFixture _database;

        public BuildController20190116Tests(ITestOutputHelper output, TestDatabaseFixture database)
        {
            _output = output;
            _database = database;
        }

        [Fact]
        public async Task EmptyBuildIsCreatedAndCanRetrieved()
        {
            using TestData data = await BuildDefaultAsync();

            string commitHash = "FAKE-COMMIT";
            string account = "FAKE-ACCOUNT";
            string project = "FAKE-PROJECT";
            string buildNumber = "20.5.19.20";
            string repository = "FAKE-REPOSITORY";
            string branch = "FAKE-BRANCH";

            int id;
            {
                IActionResult result = await data.Controller.Create(new BuildData
                {
                    Commit = commitHash,
                    AzureDevOpsAccount = account,
                    AzureDevOpsProject = project,
                    AzureDevOpsBuildNumber = buildNumber,
                    AzureDevOpsRepository = repository,
                    AzureDevOpsBranch = branch,
                });

                Assert.IsAssignableFrom<ObjectResult>(result);
                var objResult = (ObjectResult) result;
                Assert.Equal((int) HttpStatusCode.Created, objResult.StatusCode);
                Assert.IsAssignableFrom<Build>(objResult.Value);
                var build = (Build) objResult.Value;

                id = build.Id;
                Assert.Equal(commitHash, build.Commit);
                Assert.Equal(account, build.AzureDevOpsAccount);
                Assert.Equal(project, build.AzureDevOpsProject);
                Assert.Equal(buildNumber, build.AzureDevOpsBuildNumber);
                Assert.Equal(repository, build.AzureDevOpsRepository);
                Assert.Equal(branch, build.AzureDevOpsBranch);
            }

            {
                var result = await data.Controller.GetBuild(id);
                Assert.IsAssignableFrom<ObjectResult>(result);
                var objResult = (ObjectResult) result;
                Assert.Equal((int) HttpStatusCode.OK, objResult.StatusCode);
                Assert.IsAssignableFrom<Build>(objResult.Value);
                var build = (Build) objResult.Value;
                Assert.Equal(commitHash, build.Commit);
                Assert.Equal(account, build.AzureDevOpsAccount);
                Assert.Equal(project, build.AzureDevOpsProject);
                Assert.Equal(buildNumber, build.AzureDevOpsBuildNumber);
                Assert.Equal(repository, build.AzureDevOpsRepository);
                Assert.Equal(branch, build.AzureDevOpsBranch);
            }
        }

        [Fact]
        public async Task NonsenseBuildIdReturnsNotFound()
        {
            using TestData data = await BuildDefaultAsync();
            var result = await data.Controller.GetBuild(-99999);
            Assert.IsAssignableFrom<StatusCodeResult>(result);
            Assert.Equal((int) HttpStatusCode.NotFound, ((StatusCodeResult) result).StatusCode);
        }

        [Fact]
        public async Task BuildWithDependenciesIsRegistered()
        {
            using TestData data = await BuildDefaultAsync();

            string commitHash = "FAKE-COMMIT";
            string account = "FAKE-ACCOUNT";
            string project = "FAKE-PROJECT";
            string buildNumber = "20.5.19.20";
            string repository = "FAKE-REPOSITORY";
            string branch = "FAKE-BRANCH";
            
            Build aBuild;
            Build bBuild;
            {
                IActionResult result = await data.Controller.Create(new BuildData
                {
                    Commit = commitHash,
                    AzureDevOpsAccount = account,
                    AzureDevOpsProject = project,
                    AzureDevOpsBuildNumber = buildNumber + ".1",
                    AzureDevOpsRepository = "A-REPO",
                    AzureDevOpsBranch = branch,
                });
                aBuild = (Build) ((ObjectResult) result).Value;
            }
            data.Clock.UtcNow += TimeSpan.FromHours(1);
            {
                IActionResult result = await data.Controller.Create(new BuildData
                {
                    Commit = commitHash,
                    AzureDevOpsAccount = account,
                    AzureDevOpsProject = project,
                    AzureDevOpsBuildNumber = buildNumber + ".2",
                    AzureDevOpsRepository = "B-REPO",
                    AzureDevOpsBranch = branch,
                });
                bBuild = (Build) ((ObjectResult) result).Value;
            }
            data.Clock.UtcNow += TimeSpan.FromHours(1);
            {
                IActionResult result = await data.Controller.Create(new BuildData
                {
                    Commit = commitHash,
                    AzureDevOpsAccount = account,
                    AzureDevOpsProject = project,
                    AzureDevOpsBuildNumber = buildNumber + ".3",
                    AzureDevOpsRepository = "C-REPO",
                    AzureDevOpsBranch = branch,
                    Dependencies = new List<BuildRef>
                    {
                        new BuildRef(aBuild.Id, isProduct: true),
                        new BuildRef(bBuild.Id, isProduct: true),
                    },
                });
                Assert.IsAssignableFrom<ObjectResult>(result);
                var objResult = (ObjectResult) result;
                Assert.Equal((int) HttpStatusCode.Created, objResult.StatusCode);
                Assert.IsAssignableFrom<Build>(objResult.Value);
                var build = (Build) objResult.Value;
            }
        }

        private Task<TestData> BuildDefaultAsync()
        {
            return new TestDataBuilder(_database, _output).BuildAsync();
        }

        private sealed class TestDataBuilder
        {
            private readonly TestDatabaseFixture _database;
            private readonly ITestOutputHelper _output;

            public TestDataBuilder(TestDatabaseFixture database, ITestOutputHelper output)
            {
                _database = database;
                _output = output;
            }

            public async Task<TestData> BuildAsync()
            {
                string connectionString = await _database.GetConnectionString();

                ServiceCollection collection = new ServiceCollection();
                collection.AddLogging(l => l.AddProvider(new XUnitLogger(_output)));
                collection.AddSingleton<IHostEnvironment>(new HostingEnvironment
                {
                    EnvironmentName = Environments.Development
                });
                collection.AddBuildAssetRegistry(options =>
                {
                    options.UseSqlServer(connectionString);
                    options.EnableServiceProviderCaching(false);
                });
                collection.AddTransient<BuildsController>();
                collection.AddSingleton<ISystemClock, TestClock>();
                ServiceProvider provider = collection.BuildServiceProvider();

                var controller = provider.GetRequiredService<BuildsController>();
                var clock = (TestClock) provider.GetRequiredService<ISystemClock>();

                return new TestData(provider, controller, clock);
            }
        }

        private sealed class TestData : IDisposable
        {
            private readonly ServiceProvider _provider;
            public BuildsController Controller { get; }
            public TestClock Clock { get; }

            public TestData(ServiceProvider provider, BuildsController controller, TestClock clock)
            {
                _provider = provider;
                Controller = controller;
                Clock = clock;
            }

            public void Dispose()
            {
                _provider.Dispose();
            }
        }
    }
}
