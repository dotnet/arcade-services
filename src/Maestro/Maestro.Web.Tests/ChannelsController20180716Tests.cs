using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro.Data;
using Maestro.Web.Api.v2018_07_16.Controllers;
using Maestro.Web.Api.v2018_07_16.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
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
    [TestFixture]
    public partial class ChannelsController20180716Tests
    {
        [Test]
        public async Task CreateChannel()
        {
            using TestData data = await TestData.Default.BuildAsync();
            Channel channel;
            string channelName = "TEST-CHANNEL-BASIC-20180716";
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
            using TestData data = await TestData.Default.BuildAsync();
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
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult) result;
                objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
                objResult.Value.Should().BeAssignableTo<IEnumerable<string>>();
                repositories = ((IEnumerable<string>) objResult.Value).ToList();
            }

            repositories.Should().ContainSingle();
        }

        [Test]
        public async Task AddingBuildToChannelTwiceWorks()
        {
            using TestData data = await TestData.Default.BuildAsync();
            const string channelName = "TEST-CHANNEL-ADD-TWICE-2018";
            const string classification = "TEST-CLASSIFICATION";
            const string commitHash = "FAKE-COMMIT";
            const string buildNumber = "20.5.19.20";
            const string repository = "FAKE-REPOSITORY";
            const string branch = "FAKE-BRANCH";

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
                    Assets = new List<AssetData>(),
                });
                build = (Build) ((ObjectResult) result).Value;
            }

            {
                IActionResult result = await data.Controller.AddBuildToChannel(channel.Id, build.Id);
                result.Should().BeAssignableTo<StatusCodeResult>();
                var objResult = (StatusCodeResult) result;
                objResult.StatusCode.Should().Be((int) HttpStatusCode.Created);
            }

            {
                IActionResult result = await data.Controller.AddBuildToChannel(channel.Id, build.Id);
                result.Should().BeAssignableTo<StatusCodeResult>();
                var objResult = (StatusCodeResult) result;
                objResult.StatusCode.Should().Be((int) HttpStatusCode.Created);
            }
        }

        [TestDependencyInjectionSetup]
        private static class TestDataConfiguration
        {
            public static async Task Dependencies(IServiceCollection collection)
            {
                string connectionString = await SharedData.Database.GetConnectionString();
                collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
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
                collection.AddSingleton(Mock.Of<IRemoteFactory>());
                collection.AddSingleton<IBackgroundQueue, NeverBackgroundQueue>();
            }

            public static Func<IServiceProvider, TestClock> Clock(IServiceCollection collection)
            {
                collection.AddSingleton<ISystemClock, TestClock>();
                return s=> (TestClock) s.GetRequiredService<ISystemClock>();
            }

            public static Func<IServiceProvider, ChannelsController> Controller(IServiceCollection collection)
            {
                collection.AddSingleton<ChannelsController>();
                return s=> s.GetRequiredService<ChannelsController>();
            }

            public static Func<IServiceProvider, BuildsController> BuildsController(IServiceCollection collection)
            {
                collection.AddSingleton<BuildsController>();
                return s=> s.GetRequiredService<BuildsController>();
            }
        }
    }
}
