// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Common;
using Maestro.Data;
using Maestro.Web.Api.v2020_02_20.Controllers;
using Maestro.Web.Api.v2020_02_20.Models;
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
using NUnit.Framework;
using static Maestro.Web.Api.v2020_02_20.Models.DefaultChannel;

namespace Maestro.Web.Tests
{
    [TestFixture]
    public class DefaultChannelsController20200220Tests
    {
        [Test]
        public async Task CreateAndGetDefaultChannel()
        {
            using TestData data = await BuildDefaultAsync();
            string channelName = "TEST-CHANNEL-LIST-REPOSITORIES";
            string classification = "TEST-CLASSIFICATION";
            string repository = "FAKE-REPOSITORY";
            string branch = "FAKE-BRANCH";

            Channel channel1, channel2;
            {
                var result = await data.ChannelsController.CreateChannel($"{channelName}-1", classification);
                channel1 = (Channel) ((ObjectResult) result).Value;
                result = await data.ChannelsController.CreateChannel($"{channelName}-2", classification);
                channel2 = (Channel) ((ObjectResult) result).Value;
            }

            DefaultChannel defaultChannel;
            {
                DefaultChannelCreateData testDefaultChannelData = new DefaultChannelCreateData()
                {
                    Branch = branch,
                    ChannelId = channel2.Id,
                    Enabled = true,
                    Repository = repository
                };
                var result = await data.DefaultChannelsController.Create(testDefaultChannelData);
                defaultChannel = (DefaultChannel) ((ObjectResult) result).Value;
            }

            DefaultChannel singleChannelGetDefaultChannel;
            {
                IActionResult result = await data.DefaultChannelsController.Get(defaultChannel.Id);
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult) result;
                objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
                objResult.Value.Should().BeAssignableTo<DefaultChannel>();
                singleChannelGetDefaultChannel = ((DefaultChannel) objResult.Value);
            }
            singleChannelGetDefaultChannel.Id.Should().Be(defaultChannel.Id);

            List<DefaultChannel> listOfInsertedDefaultChannels;
            {
                IActionResult result = data.DefaultChannelsController.List(repository, branch, channel2.Id);
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult) result;
                objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
                objResult.Value.Should().BeAssignableTo<IEnumerable<DefaultChannel>>();
                listOfInsertedDefaultChannels = ((IEnumerable<DefaultChannel>) objResult.Value).ToList();
            }

            listOfInsertedDefaultChannels.Should().ContainSingle();
            listOfInsertedDefaultChannels.Single().Channel.Id.Should().Be(channel2.Id, "Only fake channel #2's id should show up as a default channel");
        }

        [Test]
        public async Task UpdateDefaultChannel()
        {
            using TestData data = await BuildDefaultAsync();
            string channelName = "TEST-CHANNEL-TO-UPDATE";
            string classification = "TEST-CLASSIFICATION";
            string repository = "FAKE-REPOSITORY";
            string branch = "FAKE-BRANCH";

            Channel channel1, channel2;
            {
                var result = await data.ChannelsController.CreateChannel($"{channelName}-1", classification);
                channel1 = (Channel) ((ObjectResult) result).Value;
                result = await data.ChannelsController.CreateChannel($"{channelName}-2", classification);
                channel2 = (Channel) ((ObjectResult) result).Value;
            }

            DefaultChannel defaultChannel;
            {
                DefaultChannelCreateData testDefaultChannelData = new DefaultChannelCreateData()
                {
                    Branch = branch,
                    ChannelId = channel1.Id,
                    Enabled = true,
                    Repository = repository
                };
                var result = await data.DefaultChannelsController.Create(testDefaultChannelData);
                defaultChannel = (DefaultChannel) ((ObjectResult) result).Value;
            }

            DefaultChannel updatedDefaultChannel;
            {
                DefaultChannelUpdateData defaultChannelUpdateData = new DefaultChannelUpdateData()
                {
                    Branch = $"{branch}-UPDATED",
                    ChannelId = channel2.Id,
                    Enabled = false,
                    Repository = $"NEW-{repository}"
                };
                var result = await data.DefaultChannelsController.Update(defaultChannel.Id, defaultChannelUpdateData);
                updatedDefaultChannel = (DefaultChannel) ((ObjectResult) result).Value;
            }

            List<DefaultChannel> defaultChannels;
            {
                IActionResult result = data.DefaultChannelsController.List($"NEW-{repository}", $"{branch}-UPDATED", channel2.Id, false);
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult) result;
                objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
                objResult.Value.Should().BeAssignableTo<IEnumerable<DefaultChannel>>();
                defaultChannels = ((IEnumerable<DefaultChannel>) objResult.Value).ToList();
            }

            defaultChannels.Should().ContainSingle();
            defaultChannels.Single().Channel.Id.Should().Be(channel2.Id, "Only fake channel #2's id should show up as a default channel");
        }

        [Test]
        public async Task DefaultChannelRegularExpressionMatching()
        {
            using TestData data = await BuildDefaultAsync();
            string channelName = "TEST-CHANNEL-REGEX-FOR-DEFAULT";
            string classification = "TEST-CLASSIFICATION";
            string repository = "FAKE-REPOSITORY";
            string branch = "-regex:FAKE-BRANCH-REGEX-.*";

            Channel channel;
            {
                var result = await data.ChannelsController.CreateChannel($"{channelName}", classification);
                channel = (Channel) ((ObjectResult) result).Value;
            }

            DefaultChannel defaultChannel;
            {
                DefaultChannelCreateData testDefaultChannelData = new DefaultChannelCreateData()
                {
                    Branch = branch,
                    ChannelId = channel.Id,
                    Enabled = true,
                    Repository = repository
                };
                var result = await data.DefaultChannelsController.Create(testDefaultChannelData);
                defaultChannel = (DefaultChannel) ((ObjectResult) result).Value;
            }

            string[] branchesThatMatch = new string[] { "FAKE-BRANCH-REGEX-", "FAKE-BRANCH-REGEX-RELEASE-BRANCH-1", "FAKE-BRANCH-REGEX-RELEASE-BRANCH-2" };
            string[] branchesThatDontMatch = new string[] { "I-DONT-MATCH", "REAL-BRANCH-REGEX" };

            foreach (string branchName in branchesThatMatch)
            {
                List<DefaultChannel> defaultChannels;
                {
                    IActionResult result = data.DefaultChannelsController.List(repository, branchName, channel.Id);
                    result.Should().BeAssignableTo<ObjectResult>();
                    var objResult = (ObjectResult) result;
                    objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
                    objResult.Value.Should().BeAssignableTo<IEnumerable<DefaultChannel>>();
                    defaultChannels = ((IEnumerable<DefaultChannel>) objResult.Value).ToList();
                }
                defaultChannels.Should().ContainSingle();
                defaultChannels.Single().Channel.Id.Should().Be(channel.Id);
            }

            foreach (string branchName in branchesThatDontMatch)
            {
                List<DefaultChannel> defaultChannels;
                {
                    IActionResult result = data.DefaultChannelsController.List(repository, branchName, channel.Id);
                    result.Should().BeAssignableTo<ObjectResult>();
                    var objResult = (ObjectResult) result;
                    objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
                    objResult.Value.Should().BeAssignableTo<IEnumerable<DefaultChannel>>();
                    defaultChannels = ((IEnumerable<DefaultChannel>) objResult.Value).ToList();
                }
                defaultChannels.Should().BeEmpty();
            }
        }

        [Test]
        public async Task TryToAddNonExistentChannel()
        {
            using TestData data = await BuildDefaultAsync();
            string repository = "FAKE-REPOSITORY";
            string branch = "FAKE-BRANCH";

            DefaultChannelCreateData testDefaultChannelData = new DefaultChannelCreateData()
            {
                Branch = branch,
                ChannelId = 404,
                Enabled = true,
                Repository = repository
            };
            var result = await data.DefaultChannelsController.Create(testDefaultChannelData);
            result.Should().BeOfType<NotFoundObjectResult>("Asking for a non-existent channel should give a not-found-object type result");
        }

        [Test]
        public async Task TryToGetOrUpdateNonExistentChannel()
        {
            string channelName = "TEST-CHANNEL-TO-UPDATE";
            string classification = "TEST-CLASSIFICATION";
            string repository = "FAKE-NON-EXISTENT-REPOSITORY-MISSING-CHANNEL-UPDATE";
            string branch = "FAKE-BRANCH-MISSING-CHANNEL-UPDATE";

            using TestData data = await BuildDefaultAsync();
            DefaultChannelUpdateData defaultChannelThatDoesntExistUpdateData = new DefaultChannelUpdateData()
            {
                Branch = branch,
                ChannelId = 404,
                Enabled = false,
                Repository = repository
            };
            // First: non-existent default channel
            var expectedFailResult = await data.DefaultChannelsController.Update(404, defaultChannelThatDoesntExistUpdateData);
            expectedFailResult.Should().BeOfType<NotFoundResult>("Asking for a non-existent channel should give a not-found type result");

            // Second: Extant default, non-existent channel.
            Channel channel;
            {
                var result = await data.ChannelsController.CreateChannel(channelName, classification);
                channel = (Channel) ((ObjectResult) result).Value;
            }

            DefaultChannel defaultChannel;
            {
                DefaultChannelCreateData testDefaultChannelData = new DefaultChannelCreateData()
                {
                    Branch = branch,
                    ChannelId = channel.Id,
                    Enabled = true,
                    Repository = repository
                };
                var result = await data.DefaultChannelsController.Create(testDefaultChannelData);
                defaultChannel = (DefaultChannel) ((ObjectResult) result).Value;
            }

            DefaultChannelUpdateData defaultChannelUpdateData = new DefaultChannelUpdateData()
            {
                Branch = $"{branch}-UPDATED",
                ChannelId = 404,
                Enabled = false,
                Repository = $"NEW-{repository}"
            };
            var secondExpectedFailResult = await data.DefaultChannelsController.Update(defaultChannel.Id, defaultChannelUpdateData);
            secondExpectedFailResult.Should().BeOfType<NotFoundObjectResult>("Updating a default channel for a non-existent channel should give a not-found type result");
            // Try to get a default channel that just doesn't exist at all.
            var thirdExpectedFailResult = await data.DefaultChannelsController.Get(404);
            thirdExpectedFailResult.Should().BeOfType<NotFoundResult>("Getting a default channel for a non-existent default channel should give a not-found type result");
        }

        [Test]
        public async Task AddDuplicateDefaultChannels()
        {
            using TestData data = await BuildDefaultAsync();
            string channelName = "TEST-CHANNEL-DUPLICATE-ENTRY-SCENARIO";
            string classification = "TEST-CLASSIFICATION";
            string repository = "FAKE-REPOSITORY";
            string branch = "FAKE-BRANCH";

            Channel channel;
            {
                var result = await data.ChannelsController.CreateChannel(channelName, classification);
                channel = (Channel) ((ObjectResult) result).Value;
            }

            DefaultChannelCreateData testDefaultChannelData = new DefaultChannelCreateData()
            {
                Branch = branch,
                ChannelId = channel.Id,
                Enabled = true,
                Repository = repository
            };

            DefaultChannel defaultChannel;
            {
                var result = await data.DefaultChannelsController.Create(testDefaultChannelData);
                defaultChannel = (DefaultChannel) ((ObjectResult) result).Value;
            }

            defaultChannel.Should().NotBeNull();

            DefaultChannel defaultChannelDuplicateAdd;
            {
                var result = await data.DefaultChannelsController.Create(testDefaultChannelData);
                defaultChannelDuplicateAdd = (DefaultChannel) ((ObjectResult) result).Value;
            }

            // Adding the same thing twice should succeed, as well as provide the correct object in return.
            defaultChannelDuplicateAdd.Should().NotBeNull();
            defaultChannelDuplicateAdd.Id.Should().Be(defaultChannel.Id);
            defaultChannelDuplicateAdd.Branch.Should().Be(defaultChannel.Branch);
            defaultChannelDuplicateAdd.Channel.Id.Should().Be(defaultChannel.Channel.Id);
            defaultChannelDuplicateAdd.Repository.Should().Be(defaultChannel.Repository);
        }

        private Task<TestData> BuildDefaultAsync()
        {
            return new TestDataBuilder().BuildAsync();
        }

        private sealed class TestDataBuilder
        {
            private Type _backgroundQueueType = typeof(NeverBackgroundQueue);

            public TestDataBuilder WithImmediateBackgroundQueue()
            {
                _backgroundQueueType = typeof(ImmediateBackgroundQueue);
                return this;
            }

            public async Task<TestData> BuildAsync()
            {
                string connectionString = await SharedData.Database.GetConnectionString();

                var collection = new ServiceCollection();
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
                collection.AddSingleton<DefaultChannelsController>();
                collection.AddSingleton<ChannelsController>();
                collection.AddSingleton<BuildsController>();
                collection.AddSingleton<ISystemClock, TestClock>();
                collection.AddSingleton(Mock.Of<IRemoteFactory>());
                collection.AddSingleton(typeof(IBackgroundQueue), _backgroundQueueType);
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

            public DefaultChannelsController DefaultChannelsController => _provider.GetRequiredService<DefaultChannelsController>();
            public ChannelsController ChannelsController => _provider.GetRequiredService<ChannelsController>();
            public BuildsController BuildsController => _provider.GetRequiredService<BuildsController>();

            public void Dispose()
            {
                _provider.Dispose();
            }
        }
    }
}
