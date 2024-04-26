// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;

namespace SubscriptionActorService.Tests;

[TestFixture]
public abstract class SubscriptionOrPullRequestActorTests : ActorTests
{
    protected const string AssetFeedUrl = "https://source.feed/index.json";
    protected const string SourceBranch = "source.branch";
    protected const string SourceRepo = "source.repo";
    protected const string TargetRepo = "target.repo";
    protected const string TargetBranch = "target.branch";
    protected const string NewBuildNumber = "build.number";
    protected const string NewCommit = "sha2";

    protected Mock<IActionRunner> ActionRunner = null!;
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
        AfterDbUpdateActions= [];
        ActionRunner = CreateMock<IActionRunner>();
        HostingEnvironment = CreateMock<IHostEnvironment>();
    }

    protected override void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(HostingEnvironment.Object);
        services.AddSingleton(ActionRunner.Object);
        services.AddBuildAssetRegistry(options =>
        {
            options.UseInMemoryDatabase("BuildAssetRegistry");
            options.EnableServiceProviderCaching(false);
        });
        base.RegisterServices(services);
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
    {
        Subscription = new Subscription
        {
            Channel = Channel,
            SourceRepository = SourceRepo,
            TargetRepository = TargetRepo,
            TargetBranch = TargetBranch,
            PolicyObject = policy
        };
        ContextUpdates.Add(context => context.Subscriptions.Add(Subscription));
    }

    internal void GivenACodeFlowSubscription(SubscriptionPolicy policy)
    {
        Subscription = new Subscription
        {
            Channel = Channel,
            SourceRepository = SourceRepo,
            TargetRepository = TargetRepo,
            TargetBranch = TargetBranch,
            PolicyObject = policy,

            SourceEnabled = true,
            SourceDirectory = "repo",
            ExcludedAssets = [ new AssetFilter() { Filter = "Excluded.Package" }],
        };
        ContextUpdates.Add(context => context.Subscriptions.Add(Subscription));
    }

    internal Build GivenANewBuild(bool addToChannel, (string name, string version, bool nonShipping)[]? assets = null)
    {
        assets ??= [("quail.eating.ducks", "1.1.0", false), ("quail.eating.ducks", "1.1.0", false), ("quite.expensive.device", "2.0.1", true)];
        var build = new Build
        {
            GitHubBranch = SourceBranch,
            GitHubRepository = SourceRepo,
            AzureDevOpsBuildNumber = NewBuildNumber,
            AzureDevOpsBranch = SourceBranch,
            AzureDevOpsRepository = SourceRepo,
            Commit = NewCommit,
            DateProduced = DateTimeOffset.UtcNow,
            Assets = new List<Asset>(
                assets.Select(
                    a => new Asset
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
                    }))
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
}
