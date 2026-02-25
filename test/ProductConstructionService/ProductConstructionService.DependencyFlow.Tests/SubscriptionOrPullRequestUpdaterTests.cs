
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
internal abstract class SubscriptionOrPullRequestUpdaterTests : UpdaterTests
{
    protected List<Action<BuildAssetRegistryContext>> ContextUpdates = null!;
    protected List<Action> AfterDbUpdateActions = null!;
    protected Mock<IHostEnvironment> HostingEnvironment = null!;
    protected Channel Channel = null!;
    protected DefaultChannel DefaultChannel = null!;
    protected Subscription Subscription = null!;

    [SetUp]
    public void SubscriptionOrPullRequestActorTests_SetUp()
    {
        ContextUpdates = [];
        AfterDbUpdateActions = [];
        HostingEnvironment = CreateMock<IHostEnvironment>();
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.AddSingleton(UpdateResolver.Object);
        services.AddSingleton(HostingEnvironment.Object);
        services.AddDbContext<BuildAssetRegistryContext>(options =>
        {
            options.UseInMemoryDatabase("BuildAssetRegistry");
            options.EnableServiceProviderCaching(false);
        });
    }

    protected override async Task BeforeExecute(IServiceProvider context)
    {
        var dbContext = context.GetRequiredService<BuildAssetRegistryContext>();
        foreach (Action<BuildAssetRegistryContext> update in ContextUpdates)
        {
            update(dbContext);
        }

        await dbContext.SaveChangesAsync();

        foreach (Action update in AfterDbUpdateActions)
        {
            update();
        }
    }

    internal void GivenADefaultChannel(bool enabled)
    {
        DefaultChannel = new DefaultChannel
        {
            Branch = SourceBranch,
            Channel = Channel,
            ChannelId = Channel.Id,
            Enabled = enabled,
            Repository = SourceRepo
        };
        ContextUpdates.Add(context => context.DefaultChannels.Add(DefaultChannel));
    }

    internal void GivenATestChannel()
    {
        Channel = new Channel
        {
            Name = "channel",
            Classification = "class"
        };
        ContextUpdates.Add(context => context.Channels.Add(Channel));
    }

    internal void GivenASubscription(SubscriptionPolicy policy)
        => GivenASubscription(policy, []);

    internal void GivenASubscription(SubscriptionPolicy policy, params string[] excludedAssetPatterns)
        => GivenASubscription(policy, null, excludedAssetPatterns);

    internal void GivenASubscription(SubscriptionPolicy policy, string? targetDirectory, params string[] excludedAssetPatterns)
    {
        Subscription = new Subscription
        {
            Channel = Channel,
            SourceRepository = SourceRepo,
            TargetRepository = TargetRepo,
            TargetBranch = TargetBranch,
            TargetDirectory = targetDirectory,
            PolicyObject = policy,
            Id = Guid.NewGuid(),
            ExcludedAssets = [.. excludedAssetPatterns.Select(pattern => new AssetFilter { Filter = pattern })]
        };
        ContextUpdates.Add(context => context.Subscriptions.Add(Subscription));
    }

    internal void GivenACodeFlowSubscription(SubscriptionPolicy policy)
    {
        Subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Channel = Channel,
            SourceRepository = SourceRepo,
            TargetRepository = VmrUri,
            TargetBranch = TargetBranch,
            PolicyObject = policy,

            SourceEnabled = true,
            TargetDirectory = "repo",
            ExcludedAssets = [new AssetFilter() { Filter = "Excluded.Package" }],
        };
        ContextUpdates.Add(context => context.Subscriptions.Add(Subscription));
    }

    internal Build GivenANewBuild(bool addToChannel, (string name, string version, bool nonShipping)[]? assets = null)
    {
        assets ??= [("quail.eating.ducks", "1.1.0", false), ("quite.expensive.device", "2.0.1", true)];
        var build = new Build
        {
            GitHubBranch = SourceBranch,
            GitHubRepository = SourceRepo,
            AzureDevOpsBuildNumber = NewBuildNumber,
            AzureDevOpsBranch = SourceBranch,
            AzureDevOpsRepository = SourceRepo,
            Commit = NewCommit,
            DateProduced = DateTimeOffset.UtcNow,
            Assets =
            [
                ..assets.Select(a => new Maestro.Data.Models.Asset
                {
                    Name = a.name,
                    Version = a.version,
                    NonShipping = a.nonShipping,
                    Locations =
                    [
                        new AssetLocation
                        {
                            Location = AssetFeedUrl,
                            Type = LocationType.NugetFeed
                        }
                    ]
                })
            ]
        };
        ContextUpdates.Add(
            context =>
            {
                context.Builds.Add(build);
                if (addToChannel)
                {
                    context.BuildChannels.Add(
                        new BuildChannel
                        {
                            Build = build,
                            Channel = Channel,
                            DateTimeAdded = DateTimeOffset.UtcNow
                        });
                }
            });
        return build;
    }

    protected PullRequestUpdaterId GetPullRequestUpdaterId()
    {
        return Subscription.PolicyObject.Batchable
            ? new BatchedPullRequestUpdaterId(Subscription.TargetRepository, Subscription.TargetBranch)
            : new NonBatchedPullRequestUpdaterId(Subscription.Id);
    }
}
