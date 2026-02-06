// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using AwesomeAssertions;
using Maestro.Data;
using Maestro.DataProviders;
using Maestro.DataProviders.ConfigurationIngestion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Api.v2020_02_20.Controllers;
using ProductConstructionService.Api.Controllers;
using ProductConstructionService.Api.v2020_02_20.Models;
using ProductConstructionService.Common;
using ProductConstructionService.Common.Cache;
using ProductConstructionService.DependencyFlow.Tests.Mocks;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public partial class DefaultChannelsController20200220Tests
{
    [Test]
    public async Task CreateAndGetDefaultChannel()
    {
        using TestData data = await TestData.Default.BuildAsync();
        var channelName1 = "TEST-CHANNEL-LIST-REPOSITORIES1";
        var channelName2 = "TEST-CHANNEL-LIST-REPOSITORIES2";
        var classification = "TEST-CLASSIFICATION";
        var repository = "FAKE-REPOSITORY";
        var branch = "FAKE-BRANCH";

        var yamlConfiguration = new YamlConfiguration(
            Subscriptions: [],
            Channels: [
                    new ChannelYaml { Name = channelName1, Classification = classification },
                    new ChannelYaml { Name = channelName2, Classification = classification }
            ],
            DefaultChannels: [
                new DefaultChannelYaml { Branch = branch, Channel = channelName2, Enabled = true, Repository = repository  },
            ],
            BranchMergePolicies: []);

        DefaultChannelYaml defaultChannelYaml;
        {
            var result = await data.ConfigurationIngestionController.IngestNamespace(
                nameof(CreateAndGetDefaultChannel),
                yamlConfiguration);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<ConfigurationUpdates>();
            var configUpdates = (ConfigurationUpdates)objResult.Value!;

            configUpdates.Channels.Creations.Should().HaveCount(2);
            configUpdates.DefaultChannels.Creations.Should().HaveCount(1);

            defaultChannelYaml = configUpdates.DefaultChannels.Creations.First();
        }

        Channel channel2;
        {
            var result = data.ChannelsController.ListChannels();
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<IEnumerable<Channel>>();
            var channels = (IEnumerable<Channel>)objResult.Value!;
            channel2 = channels.First(c => c.Name == channelName2);
        }

        List<DefaultChannel> listOfInsertedDefaultChannels;
        {
            IActionResult result = data.DefaultChannelsController.List(repository, branch, channel2.Id);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<IEnumerable<DefaultChannel>>();
            listOfInsertedDefaultChannels = [.. ((IEnumerable<DefaultChannel>)objResult.Value!)];
            listOfInsertedDefaultChannels.Should().HaveCount(1);
        }

        DefaultChannel singleChannelGetDefaultChannel;
        {
            IActionResult result = await data.DefaultChannelsController.Get(listOfInsertedDefaultChannels.First().Id);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<DefaultChannel>();
            singleChannelGetDefaultChannel = (DefaultChannel)objResult.Value!;
        }

        listOfInsertedDefaultChannels.Single().Channel.Id.Should().Be(channel2.Id, "Only fake channel #2's id should show up as a default channel");
    }

    [Test]
    public async Task DefaultChannelRegularExpressionMatching()
    {
        using TestData data = await TestData.Default.BuildAsync();
        var channelName = "TEST-CHANNEL-REGEX-FOR-DEFAULT";
        var classification = "TEST-CLASSIFICATION";
        var repository = "FAKE-REPOSITORY";
        var branch = "-regex:FAKE-BRANCH-REGEX-.*";

        var yamlConfiguration = new YamlConfiguration(
            Subscriptions: [],
            Channels: [
                    new ChannelYaml { Name = channelName, Classification = classification }
            ],
            DefaultChannels: [
                new DefaultChannelYaml { Branch = branch, Channel = channelName, Enabled = true, Repository = repository  },
            ],
            BranchMergePolicies: []);
        {
            var result = await data.ConfigurationIngestionController.IngestNamespace(
                nameof(DefaultChannelRegularExpressionMatching),
                yamlConfiguration);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
        }

        Channel channel;
        {
            var result = data.ChannelsController.ListChannels();
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<IEnumerable<Channel>>();
            var channels = (IEnumerable<Channel>)objResult.Value!;
            channel = channels.First(c => c.Name == channelName);
        }

        string[] branchesThatMatch = ["FAKE-BRANCH-REGEX-", "FAKE-BRANCH-REGEX-RELEASE-BRANCH-1", "FAKE-BRANCH-REGEX-RELEASE-BRANCH-2"];
        string[] branchesThatDontMatch = ["I-DONT-MATCH", "REAL-BRANCH-REGEX"];

        foreach (var branchName in branchesThatMatch)
        {
            List<DefaultChannel> defaultChannels;
            {
                IActionResult result = data.DefaultChannelsController.List(repository, branchName, channel.Id);
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult)result;
                objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
                objResult.Value.Should().BeAssignableTo<IEnumerable<DefaultChannel>>();
                defaultChannels = [.. ((IEnumerable<DefaultChannel>)objResult.Value!)];
            }
            defaultChannels.Should().ContainSingle();
            defaultChannels.Single().Channel.Id.Should().Be(channel.Id);
        }

        foreach (var branchName in branchesThatDontMatch)
        {
            List<DefaultChannel> defaultChannels;
            {
                IActionResult result = data.DefaultChannelsController.List(repository, branchName, channel.Id);
                result.Should().BeAssignableTo<ObjectResult>();
                var objResult = (ObjectResult)result;
                objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
                objResult.Value.Should().BeAssignableTo<IEnumerable<DefaultChannel>>();
                defaultChannels = [.. ((IEnumerable<DefaultChannel>)objResult.Value!)];
            }
            defaultChannels.Should().BeEmpty();
        }
    }

    [Test]
    public async Task TryToGetNonExistentChannel()
    {
        using TestData data = await TestData.Default.BuildAsync();

        // Try to get a default channel that just doesn't exist at all.
        var thirdExpectedFailResult = await data.DefaultChannelsController.Get(404);
        thirdExpectedFailResult.Should().BeOfType<NotFoundResult>("Getting a default channel for a non-existent default channel should give a not-found type result");
    }

    [TestDependencyInjectionSetup]
    private static class TestDataConfiguration
    {
        public static async Task Dependencies(IServiceCollection collection)
        {
            var connectionString = await SharedData.Database.GetConnectionString();

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
            collection.AddSingleton(Mock.Of<IRemoteFactory>());
            collection.AddSingleton(Mock.Of<IBasicBarClient>());

            collection.AddSingleton<IOptions<EnvironmentNamespaceOptions>>(
                new OptionsWrapper<EnvironmentNamespaceOptions>(
                    new EnvironmentNamespaceOptions
                    {
                        DefaultNamespaceName = TestDatabase.TestNamespace
                    }));
        }

        public static void AddConfigurationIngestor(IServiceCollection collection)
        {
            var installationIdResolver = new Mock<IGitHubInstallationIdResolver>();
            installationIdResolver.Setup(r => r.GetInstallationIdForRepository(It.IsAny<string>()))
                .Returns(Task.FromResult((long?)1));
            var ghTagValidator = new Mock<IGitHubTagValidator>();
            ghTagValidator.Setup(v => v.IsNotificationTagValidAsync(It.IsAny<string>()))
                .Returns(Task.FromResult(true));
            var kustoClientProvider = new Mock<IKustoClientProvider>();

            collection.AddSingleton(installationIdResolver.Object)
                .AddSingleton<IConfigurationIngestor, ConfigurationIngestor>()
                .AddSingleton(kustoClientProvider.Object)
                .AddSingleton(ghTagValidator.Object)
                .AddSingleton<ISqlBarClient, SqlBarClient>()
                .AddSingleton<IDistributedLock, DistributedLock>()
                .AddSingleton<IRedisCacheFactory, MockRedisCacheFactory>();
        }

        public static Func<IServiceProvider, TestClock> Clock(IServiceCollection collection)
        {
            collection.AddSingleton<ISystemClock, TestClock>();
            return s => (TestClock)s.GetRequiredService<ISystemClock>();
        }

        public static Func<IServiceProvider, ChannelsController> ChannelsController(IServiceCollection collection)
        {
            collection.AddSingleton<ChannelsController>();
            return s => s.GetRequiredService<ChannelsController>();
        }

        public static Func<IServiceProvider, DefaultChannelsController> DefaultChannelsController(
            IServiceCollection collection)
        {
            collection.AddSingleton<DefaultChannelsController>();
            return s => s.GetRequiredService<DefaultChannelsController>();
        }

        public static Func<IServiceProvider, ConfigurationIngestionController> ConfigurationIngestionController(IServiceCollection collection)
        {
            collection.AddSingleton<ConfigurationIngestionController>();
            return s => s.GetRequiredService<ConfigurationIngestionController>();
        }
    }
}
