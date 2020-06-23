using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Web.Api.v2018_07_16.Controllers;
using Maestro.Web.Api.v2018_07_16.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Maestro.Web.Tests
{
    [Collection(nameof(DatabaseCollection))]
    public class ChannelsController20180716Tests
    {
        private readonly ITestOutputHelper _output;
        private readonly TestDatabaseFixture _database;

        public ChannelsController20180716Tests(ITestOutputHelper output, TestDatabaseFixture database)
        {
            _output = output;
            _database = database;
        }

        [Fact]
        public async Task CreateChannel()
        {
            using TestData data = await BuildDefaultAsync();
            Channel channel;
            string channelName = "TEST-CHANNEL-BASIC";
            string classification = "TEST-CLASSIFICATION";
            {
                IActionResult result = await data.Controller.CreateChannel(channelName, classification);
                Assert.IsAssignableFrom<ObjectResult>(result);
                var objResult = (ObjectResult) result;
                Assert.Equal((int) HttpStatusCode.Created, objResult.StatusCode);
                Assert.IsAssignableFrom<Channel>(objResult.Value);
                channel = (Channel) objResult.Value;
                Assert.Equal(channelName, channel.Name);
                Assert.Equal(classification, channel.Classification);
            }

            {
                IActionResult result = await data.Controller.GetChannel(channel.Id);
                Assert.IsAssignableFrom<ObjectResult>(result);
                var objResult = (ObjectResult) result;
                Assert.Equal((int) HttpStatusCode.OK, objResult.StatusCode);
                Assert.IsAssignableFrom<Channel>(objResult.Value);
                channel = (Channel) objResult.Value;
                Assert.Equal(channelName, channel.Name);
                Assert.Equal(classification, channel.Classification);
            }
        }

        [Fact]
        public async Task ListRepositories()
        {
            using TestData data = await BuildDefaultAsync();
            string channelName = "TEST-CHANNEL-LIST-REPOSITORIES-20180716";
            string classification = "TEST-CLASSIFICATION";
            string commitHash = "FAKE-COMMIT";
            string buildNumber = "20.5.19.20";
            string repository = "FAKE-REPOSITORY";
            string branch = "FAKE-BRANCH";

            Channel channel;
            {
                var result = await data.Controller.CreateChannel(channelName, classification);
                channel = (Channel) ((ObjectResult) result).Value;
            }

            Build build;
            {
                IActionResult result = await data.BuildsController.Create(new BuildData
                {
                    Commit = commitHash,
                    BuildNumber = buildNumber,
                    Repository = repository,
                    Branch = branch,
                    Assets = new List<AssetData>()
                }) ;
                build = (Build) ((ObjectResult) result).Value;
            }

            await data.Controller.AddBuildToChannel(channel.Id, build.Id);

            List<string> repositories;
            {
                IActionResult result = await data.Controller.ListRepositories(channel.Id);
                Assert.IsAssignableFrom<ObjectResult>(result);
                var objResult = (ObjectResult) result;
                Assert.Equal((int) HttpStatusCode.OK, objResult.StatusCode);
                Assert.IsAssignableFrom<IEnumerable<string>>(objResult.Value);
                repositories = ((IEnumerable<string>) objResult.Value).ToList();
            }

            Assert.Single(repositories, repository);
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

            private Type _backgroundQueueType = typeof(NeverBackgroundQueue);

            public TestDataBuilder WithImmediateBackgroundQueue()
            {
                _backgroundQueueType = typeof(ImmediateBackgroundQueue);
                return this;
            }

            public async Task<TestData> BuildAsync()
            {
                string connectionString = await _database.GetConnectionString();

                var collection = new ServiceCollection();
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
                collection.AddSingleton<ChannelsController>();
                collection.AddSingleton<BuildsController>();
                collection.AddSingleton<ISystemClock, TestClock>();
                collection.AddSingleton(Mock.Of<IRemoteFactory>());
                collection.AddSingleton(typeof(IBackgroundQueue), _backgroundQueueType);
                collection.AddSingleton(_output);
                ServiceProvider provider = collection.BuildServiceProvider();

                var clock = (TestClock) provider.GetRequiredService<ISystemClock>();

                return new TestData(provider, clock);
            }
        }

        private sealed class TestData : IDisposable
        {
            private readonly ServiceProvider _provider;
            public TestClock Clock { get; }

            public TestData(ServiceProvider provider, TestClock clock)
            {
                _provider = provider;
                Clock = clock;
            }
            
            public ChannelsController Controller => _provider.GetRequiredService<ChannelsController>();
            public BuildsController BuildsController => _provider.GetRequiredService<BuildsController>();

            public void Dispose()
            {
                _provider.Dispose();
            }
        }
    }
}
