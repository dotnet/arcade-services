// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using AwesomeAssertions;
using Maestro.Common.Cache;
using Maestro.Data;
using Maestro.DataProviders;
using Maestro.DataProviders.ConfigurationIngestion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
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
using ProductConstructionService.Api.Api.v2018_07_16.Controllers;
using ProductConstructionService.Api.Controllers;
using ProductConstructionService.Api.v2018_07_16.Models;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Tests.Mocks;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public partial class ChannelsController20180716Tests
{
    [Test]
    public async Task ListRepositories()
    {
        using TestData data = await TestData.Default.BuildAsync();
        var channelName = "TEST-CHANNEL-LIST-REPOSITORIES-20180716";
        var classification = "TEST-CLASSIFICATION";
        var commitHash = "FAKE-COMMIT";
        var buildNumber = "20.5.19.20";
        var repository = "FAKE-REPOSITORY";
        var branch = "FAKE-BRANCH";

        var yamlConfiguration = new YamlConfiguration(
            Subscriptions: [],
            Channels: [new ChannelYaml { Name = channelName, Classification = classification }],
            DefaultChannels: [],
            BranchMergePolicies: []);
        {
            var result = await data.ConfigurationIngestionController.IngestNamespace(
                nameof(ListRepositories),
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

        Build build;
        {
            IActionResult result = await data.BuildsController.Create(new BuildData
            {
                Commit = commitHash,
                BuildNumber = buildNumber,
                Repository = repository,
                Branch = branch,
                Assets = []
            });
            build = (Build)((ObjectResult)result).Value!;
        }

        await data.ChannelsController.AddBuildToChannel(channel.Id, build.Id);

        List<string> repositories;
        {
            IActionResult result = await data.ChannelsController.ListRepositories(channel.Id);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<IEnumerable<string>>();
            repositories = [..(IEnumerable<string>)objResult.Value!];
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

        var yamlConfiguration = new YamlConfiguration(
            Subscriptions: [],
            Channels: [new ChannelYaml { Name = channelName, Classification = classification }],
            DefaultChannels: [],
            BranchMergePolicies: []);
        {
            var result = await data.ConfigurationIngestionController.IngestNamespace(
                nameof(ListRepositories),
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

        Build build;
        {
            IActionResult result = await data.BuildsController.Create(new BuildData
            {
                Commit = commitHash,
                BuildNumber = buildNumber,
                Repository = repository,
                Branch = branch,
                Assets = [],
            });
            build = (Build)((ObjectResult)result).Value!;
        }

        {
            IActionResult result = await data.ChannelsController.AddBuildToChannel(channel.Id, build.Id);
            result.Should().BeAssignableTo<StatusCodeResult>();
            var objResult = (StatusCodeResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
        }

        {
            IActionResult result = await data.ChannelsController.AddBuildToChannel(channel.Id, build.Id);
            result.Should().BeAssignableTo<StatusCodeResult>();
            var objResult = (StatusCodeResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
        }
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
            collection.AddDbContext<BuildAssetRegistryContext>(options =>
            {
                options.UseSqlServer(connectionString);
                options.EnableServiceProviderCaching(false);
            });
            collection.AddSingleton<IInstallationLookup, BuildAssetRegistryInstallationLookup>();
            collection.AddSingleton<ChannelsController>();
            collection.AddSingleton<BuildsController>();
            collection.AddSingleton(Mock.Of<IRemoteFactory>());
            collection.AddSingleton(Mock.Of<IBasicBarClient>());

            var mockWorkItemProducerFactory = new Mock<IWorkItemProducerFactory>();
            var mockWorkItemProducer = new Mock<IWorkItemProducer<BuildCoherencyInfoWorkItem>>();
            mockWorkItemProducerFactory
                .Setup(f => f.CreateProducer<BuildCoherencyInfoWorkItem>(false))
                .Returns(mockWorkItemProducer.Object);

            collection.AddSingleton(mockWorkItemProducerFactory.Object);

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

        public static Func<IServiceProvider, BuildsController> BuildsController(IServiceCollection collection)
        {
            collection.AddSingleton<BuildsController>();
            return s => s.GetRequiredService<BuildsController>();
        }

        public static Func<IServiceProvider, ConfigurationIngestionController> ConfigurationIngestionController(IServiceCollection collection)
        {
            collection.AddSingleton<ConfigurationIngestionController>();
            return s => s.GetRequiredService<ConfigurationIngestionController>();
        }
    }
}
