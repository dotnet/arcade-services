// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues.Models;
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
    private List<SubscriptionTriggerWorkItem> _updateSubscriptionWorkItems = [];

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        _updateSubscriptionWorkItems = [];
        Mock<IWorkItemProducerFactory> workItemProducerFactoryMock = new();
        Mock<IWorkItemProducer<SubscriptionTriggerWorkItem>> workItemProducerMock = new();

        workItemProducerMock.Setup(w => w.ProduceWorkItemAsync(It.IsAny<SubscriptionTriggerWorkItem>(), TimeSpan.Zero))
            .ReturnsAsync(QueuesModelFactory.SendReceipt("message", DateTimeOffset.Now, DateTimeOffset.Now, "popReceipt", DateTimeOffset.Now))
            .Callback<SubscriptionTriggerWorkItem, TimeSpan>((item, _) => _updateSubscriptionWorkItems.Add(item));
        workItemProducerFactoryMock.Setup(w => w.CreateProducer<SubscriptionTriggerWorkItem>(false))
            .Returns(workItemProducerMock.Object);

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
        services.AddSingleton(workItemProducerFactoryMock.Object);
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
        Channel channel = GetChannel();
        Build oldBuild = GetOldBuild();
        Build build = GetNewBuild();
        BuildChannel buildChannel = new()
        {
            Build = build,
            Channel = channel
        };
        Subscription subscription = GetSubscription(channel, oldBuild, true);
        await _context!.Subscriptions.AddAsync(subscription);
        await _context!.BuildChannels.AddAsync(buildChannel);
        await _context!.SaveChangesAsync();

        var triggerer = ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_scope.ServiceProvider);
        await triggerer.TriggerSubscriptionsAsync(UpdateFrequency.EveryDay);

        _updateSubscriptionWorkItems.Count.Should().Be(1);

        var item = _updateSubscriptionWorkItems[0];

        item.BuildId.Should().Be(build.Id);
        item.SubscriptionId.Should().Be(subscription.Id);
    }

    [Test]
    public async Task ShouldNotUpdateSubscriptionBecauseNotEnabled()
    {
        var channel = GetChannel();
        var oldBuild = GetOldBuild();
        var build = GetNewBuild();
        var buildChannel = new BuildChannel
        {
            Build = build,
            Channel = channel
        };
        var subscription = GetSubscription(channel, oldBuild, false);
        await _context!.Subscriptions.AddAsync(subscription);
        await _context!.BuildChannels.AddAsync(buildChannel);
        await _context!.SaveChangesAsync();

        var triggerer = ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_scope.ServiceProvider);
        await triggerer.TriggerSubscriptionsAsync(UpdateFrequency.EveryDay);

        _updateSubscriptionWorkItems.Count.Should().Be(0);
    }

    [Test]
    public async Task ShouldOnlyTriggerSubscriptionsWithCorrectUpdateFrequency()
    {
        Channel channel = GetChannel();
        Build oldBuild = GetOldBuild();
        Build build = GetNewBuild();
        BuildChannel buildChannel = new()
        {
            Build = build,
            Channel = channel
        };
        Subscription subscription = GetSubscription(channel, oldBuild, true);
        await _context!.Subscriptions.AddAsync(subscription);
        await _context!.BuildChannels.AddAsync(buildChannel);
        await _context!.SaveChangesAsync();

        var triggerer = ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_scope.ServiceProvider);
        await triggerer.TriggerSubscriptionsAsync(UpdateFrequency.EveryWeek);

        _updateSubscriptionWorkItems.Count.Should().Be(0);
    }

    [Test]
    public async Task ShouldNotTriggerUpToDateSubscription()
    {
        var channel = GetChannel();
        var build = GetNewBuild();
        var buildChannel = new BuildChannel
        {
            Build = build,
            Channel = channel
        };
        var subscription = GetSubscription(channel, build, true);
        await _context!.Subscriptions.AddAsync(subscription);
        await _context!.BuildChannels.AddAsync(buildChannel);
        await _context!.SaveChangesAsync();

        var triggerer = ActivatorUtilities.CreateInstance<SubscriptionTriggerer>(_scope.ServiceProvider);
        await triggerer.TriggerSubscriptionsAsync(UpdateFrequency.EveryDay);

        _updateSubscriptionWorkItems.Count.Should().Be(0);
    }

    private const string RepoName = "https://github.com/myorg/myrepo/";

    private static Channel GetChannel() => new()
        {
            Name = "channel",
            Classification = "class"
        };

    private static Build GetOldBuild() => new()
        {
            AzureDevOpsRepository = RepoName,
            DateProduced = DateTimeOffset.UtcNow.AddDays(-2)
        };

    private static Build GetNewBuild() => new()
        {
            AzureDevOpsRepository = RepoName,
            DateProduced = DateTimeOffset.UtcNow,
        };

    private static Subscription GetSubscription(Channel channel, Build build, bool enabled) => new()
        {
            Id = Guid.NewGuid(),
            Channel = channel,
            SourceRepository = RepoName,
            TargetRepository = "target.repo",
            TargetBranch = "target.branch",
            Enabled = enabled,
            PolicyObject = new SubscriptionPolicy
            {
                MergePolicies = null,
                UpdateFrequency = UpdateFrequency.EveryDay
            },
            LastAppliedBuild = build
        };
}
