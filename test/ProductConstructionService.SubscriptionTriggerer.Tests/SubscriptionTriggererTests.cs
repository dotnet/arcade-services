// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Kusto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.SubscriptionTriggerer.Tests;

[TestFixture]
public class SubscriptionTriggererTests
{
    private BuildAssetRegistryContext? _context;
    private ServiceProvider? _provider;
    private IServiceScope _scope = new Mock<IServiceScope>().Object;
    private Mock<IBasicBarClient> _barMock = new();

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<BuildAssetRegistryContext>(
            options =>
            {
                options.UseInMemoryDatabase("BuildAssetRegistry");
                options.EnableServiceProviderCaching(false);
            });
        services.AddSingleton(new Mock<IRemoteFactory>().Object);
        services.AddSingleton(new Mock<IBasicBarClient>().Object);
        services.AddSingleton(new Mock<IHostEnvironment>().Object);
        services.AddSingleton(new Mock<IWorkItemProducerFactory>().Object);
        services.AddSingleton(_ => new Mock<IKustoClientProvider>().Object);

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        _context = _scope.ServiceProvider.GetRequiredService<BuildAssetRegistryContext>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
        _provider!.Dispose();
    }

    [Test]
    public async Task ShouldTriggerSubscription()
    {
        Channel channel = new()
        {
            Name = "channel",
            Classification = "class"
        };
        Build oldBuild = new()
        {
            AzureDevOpsBranch = "source.branch",
            AzureDevOpsRepository = "source.repo",
            AzureDevOpsBuildNumber = "old.build.number",
            Commit = "oldSha",
            DateProduced = DateTimeOffset.UtcNow.AddDays(-2)
        };
        var location = "https://source.feed/index.json";
        Build build = new()
        {
            AzureDevOpsBranch = "source.branch",
            AzureDevOpsRepository = "source.repo",
            AzureDevOpsBuildNumber = "build.number",
            Commit = "sha",
            DateProduced = DateTimeOffset.UtcNow,
            Assets =
            [
                new Asset
                {
                    Name = "source.asset",
                    Version = "1.0.1",
                    NonShipping = false,
                    Locations =
                    [
                        new AssetLocation
                        {
                            Location = location,
                            Type = LocationType.NugetFeed
                        }
                    ]
                }
            ]
        };
        BuildChannel buildChannel = new()
        {
            Build = build,
            Channel = channel
        };
        Subscription subscription = new()
        {
            Channel = channel,
            SourceRepository = "source.repo",
            TargetRepository = "target.repo",
            TargetBranch = "target.branch",
            Enabled = true,
            PolicyObject = new SubscriptionPolicy
            {
                MergePolicies = null,
                UpdateFrequency = UpdateFrequency.EveryDay
            },
            LastAppliedBuild = oldBuild
        };
        Repository repoInstallation = new()
        {
            RepositoryName = "target.repo",
            InstallationId = 1
        };
        await _context!.Repositories.AddAsync(repoInstallation);
        await _context!.Subscriptions.AddAsync(subscription);
        await _context!.BuildChannels.AddAsync(buildChannel);
        await _context!.SaveChangesAsync();

        var triggerer = ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_scope.ServiceProvider);
        List<UpdateSubscriptionWorkItem> list = await triggerer.GetSubscriptionsToTrigger(UpdateFrequency.EveryDay);

        list.Count.Should().Be(1);

        var item = list[0];

        item.BuildId.Should().Be(build.Id);
        item.SubscriptionId.Should().Be(subscription.Id);
    }

    [Test]
    public async Task ShouldNotUpdateSubscriptionBecauseNotEnabled()
    {
        var channel = new Channel
        {
            Name = "channel",
            Classification = "class"
        };
        var oldBuild = new Build
        {
            AzureDevOpsBranch = "source.branch",
            AzureDevOpsRepository = "source.repo",
            AzureDevOpsBuildNumber = "old.build.number",
            Commit = "oldSha",
            DateProduced = DateTimeOffset.UtcNow.AddDays(-2)
        };
        var location = "https://source.feed/index.json";
        var build = new Build
        {
            AzureDevOpsBranch = "source.branch",
            AzureDevOpsRepository = "source.repo",
            AzureDevOpsBuildNumber = "build.number",
            Commit = "sha",
            DateProduced = DateTimeOffset.UtcNow,
            Assets =
            [
                new Asset
                {
                    Name = "source.asset",
                    Version = "1.0.1",
                    NonShipping = true,
                    Locations =
                    [
                        new AssetLocation
                        {
                            Location = location,
                            Type = LocationType.NugetFeed
                        }
                    ]
                }
            ]
        };
        var buildChannel = new BuildChannel
        {
            Build = build,
            Channel = channel
        };
        var subscription = new Subscription
        {
            Channel = channel,
            SourceRepository = "source.repo",
            TargetRepository = "target.repo",
            TargetBranch = "target.branch",
            Enabled = false,
            PolicyObject = new SubscriptionPolicy
            {
                MergePolicies = null,
                UpdateFrequency = UpdateFrequency.EveryDay
            },
            LastAppliedBuild = oldBuild
        };
        var repoInstallation = new Repository
        {
            RepositoryName = "target.repo",
            InstallationId = 1
        };
        await _context!.Repositories.AddAsync(repoInstallation);
        await _context!.Subscriptions.AddAsync(subscription);
        await _context!.BuildChannels.AddAsync(buildChannel);
        await _context!.SaveChangesAsync();

        var triggerer = ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_scope.ServiceProvider);
        List<UpdateSubscriptionWorkItem> list = await triggerer.GetSubscriptionsToTrigger(UpdateFrequency.EveryDay);

        list.Count.Should().Be(0);
    }

    [Test]
    public async Task ShouldOnlyTriggerSubscriptionsWIthCorrectUpdateFrequency()
    {
        Channel channel = new()
        {
            Name = "channel",
            Classification = "class"
        };
        Build oldBuild = new()
        {
            AzureDevOpsBranch = "source.branch",
            AzureDevOpsRepository = "source.repo",
            AzureDevOpsBuildNumber = "old.build.number",
            Commit = "oldSha",
            DateProduced = DateTimeOffset.UtcNow.AddDays(-2)
        };
        var location = "https://source.feed/index.json";
        Build build = new()
        {
            AzureDevOpsBranch = "source.branch",
            AzureDevOpsRepository = "source.repo",
            AzureDevOpsBuildNumber = "build.number",
            Commit = "sha",
            DateProduced = DateTimeOffset.UtcNow,
            Assets =
            [
                new Asset
                {
                    Name = "source.asset",
                    Version = "1.0.1",
                    NonShipping = false,
                    Locations =
                    [
                        new AssetLocation
                        {
                            Location = location,
                            Type = LocationType.NugetFeed
                        }
                    ]
                }
            ]
        };
        BuildChannel buildChannel = new()
        {
            Build = build,
            Channel = channel
        };
        Subscription subscription = new()
        {
            Channel = channel,
            SourceRepository = "source.repo",
            TargetRepository = "target.repo",
            TargetBranch = "target.branch",
            Enabled = true,
            PolicyObject = new SubscriptionPolicy
            {
                MergePolicies = null,
                UpdateFrequency = UpdateFrequency.EveryWeek
            },
            LastAppliedBuild = oldBuild
        };
        Repository repoInstallation = new()
        {
            RepositoryName = "target.repo",
            InstallationId = 1
        };
        await _context!.Repositories.AddAsync(repoInstallation);
        await _context!.Subscriptions.AddAsync(subscription);
        await _context!.BuildChannels.AddAsync(buildChannel);
        await _context!.SaveChangesAsync();

        var triggerer = ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_scope.ServiceProvider);
        List<UpdateSubscriptionWorkItem> list = await triggerer.GetSubscriptionsToTrigger(UpdateFrequency.EveryDay);

        list.Count.Should().Be(0);
    }

    [Test]
    public async Task ShouldNotTriggerUpToDateSubscription()
    {
        var channel = new Channel
        {
            Name = "channel",
            Classification = "class"
        };
        var build = new Build
        {
            AzureDevOpsBranch = "source.branch",
            AzureDevOpsRepository = "source.repo",
            AzureDevOpsBuildNumber = "build.number",
            Commit = "sha",
            DateProduced = DateTimeOffset.UtcNow
        };
        var buildChannel = new BuildChannel
        {
            Build = build,
            Channel = channel
        };
        var subscription = new Subscription
        {
            Channel = channel,
            SourceRepository = "source.repo",
            TargetRepository = "target.repo",
            TargetBranch = "target.branch",
            PolicyObject = new SubscriptionPolicy
            {
                MergePolicies = null,
                UpdateFrequency = UpdateFrequency.EveryDay
            },
            LastAppliedBuild = build
        };
        await _context!.Subscriptions.AddAsync(subscription);
        await _context!.BuildChannels.AddAsync(buildChannel);
        await _context!.SaveChangesAsync();

        var triggerer = ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_scope.ServiceProvider);
        List<UpdateSubscriptionWorkItem> list = await triggerer.GetSubscriptionsToTrigger(UpdateFrequency.EveryDay);

        list.Count.Should().Be(0);
    }
}
