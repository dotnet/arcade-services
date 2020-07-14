using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro.Data;
using Maestro.Web.Api.v2020_02_20.Controllers;
using Maestro.Web.Api.v2020_02_20.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Maestro.Web.Tests
{
    [TestFixture, NonParallelizable]
    public class ChannelsController20200220Tests
    {
        private readonly ITestOutputHelper _output;
        private readonly TestDatabaseFixture _database;

        [SetUp]
        public void ChannelsController20200220Tests_SetUp()
        {
            _output = output;
            _database = database;
        }

        [Test]
        public async Task CreateChannel()
        {
            using TestData data = await BuildDefaultAsync();
            Channel channel;
            string channelName = "TEST-CHANNEL-BASIC";
            string classification = "TEST-CLASSIFICATION";
            {
                IActionResult result = await data.Controller.CreateChannel(channelName, classification);
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult) result;
                objResult.StatusCode.Should().Be((int) HttpStatusCode.Created);
                objResult.Value.Should().BeAssignableTo<Channel>();
                channel = (Channel) objResult.Value;
                channel.Name.Should().Be(channelName);
                channel.Classification.Should().Be(classification);
            }

            {
                IActionResult result = await data.Controller.GetChannel(channel.Id);
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult) result;
                objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
                objResult.Value.Should().BeAssignableTo<Channel>();
                channel = (Channel) objResult.Value;
                channel.Name.Should().Be(channelName);
                channel.Classification.Should().Be(classification);
            }
        }

        [Test]
        public async Task ListRepositories()
        {
            using TestData data = await BuildDefaultAsync();
            string channelName = "TEST-CHANNEL-LIST-REPOSITORIES";
            string classification = "TEST-CLASSIFICATION";
            string commitHash = "FAKE-COMMIT";
            string account = "FAKE-ACCOUNT";
            string project = "FAKE-PROJECT";
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
                    AzureDevOpsAccount = account,
                    AzureDevOpsProject = project,
                    AzureDevOpsBuildNumber = buildNumber,
                    AzureDevOpsRepository = repository,
                    AzureDevOpsBranch = branch,
                });
                build = (Build) ((ObjectResult) result).Value;
            }

            await data.Controller.AddBuildToChannel(channel.Id, build.Id);

            List<string> repositories;
            {
                IActionResult result = await data.Controller.ListRepositories(channel.Id);
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult) result;
                objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
                objResult.Value.Should().BeAssignableTo<IEnumerable<string>>();
                repositories = ((IEnumerable<string>) objResult.Value).ToList();
            }

            repositories.Should().ContainSingle();
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
