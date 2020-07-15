// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace DependencyUpdater.Tests
{
    [TestFixture]
    public class UpdateDependenciesAsyncTests : DependencyUpdaterTests
    {
        [Test]
        public async Task EveryBuildSubscription()
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
                DateProduced = DateTimeOffset.UtcNow.AddDays(-1)
            };
            var location = "https://repo.feed/index.json";
            var newAsset = new Asset
            {
                Name = "source.asset",
                Version = "1.0.1",
                NonShipping = true,
                Locations = new List<AssetLocation>
                {
                    new AssetLocation
                    {
                        Location = location,
                        Type = LocationType.NugetFeed
                    }
                }
            };
            var newBuild = new Build
            {
                AzureDevOpsBranch = "source.branch",
                AzureDevOpsRepository = "source.repo",
                AzureDevOpsBuildNumber = "build.number.2",
                Commit = "sha2",
                DateProduced = DateTimeOffset.UtcNow,
                Assets = new List<Asset> {newAsset}
            };
            var buildChannels = new[]
            {
                new BuildChannel
                {
                    Build = build,
                    Channel = channel
                },
                new BuildChannel
                {
                    Build = newBuild,
                    Channel = channel
                }
            };
            var subscription = new Subscription
            {
                Channel = channel,
                SourceRepository = "source.repo",
                TargetRepository = "target.repo",
                TargetBranch = "target.branch",
                Enabled = true,
                PolicyObject = new SubscriptionPolicy
                {
                    MergePolicies = null,
                    UpdateFrequency = UpdateFrequency.EveryBuild
                },
                LastAppliedBuild = build
            };
            var repoInstallation = new Repository
            {
                RepositoryName = "target.repo",
                InstallationId = 1
            };
            await Context.BuildChannels.AddRangeAsync(buildChannels);
            await Context.Subscriptions.AddAsync(subscription);
            await Context.Repositories.AddAsync(repoInstallation);
            await Context.SaveChangesAsync();

            SubscriptionActor.Setup(s => s.UpdateAsync(newBuild.Id)).Returns(Task.CompletedTask);

            var updater = ActivatorUtilities.CreateInstance<DependencyUpdater>(Scope.ServiceProvider);
            await updater.UpdateDependenciesAsync(newBuild.Id, channel.Id);

            ActorId.GetGuidId().Should().Be(subscription.Id);
        }

        [Test]
        public async Task EveryBuildSubscriptionNotEnabled()
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
                DateProduced = DateTimeOffset.UtcNow.AddDays(-1)
            };
            var location = "https://repo.feed/index.json";
            var newAsset = new Asset
            {
                Name = "source.asset",
                Version = "1.0.1",
                NonShipping = false,
                Locations = new List<AssetLocation>
                {
                    new AssetLocation
                    {
                        Location = location,
                        Type = LocationType.NugetFeed
                    }
                }
            };
            var newBuild = new Build
            {
                AzureDevOpsBranch = "source.branch",
                AzureDevOpsRepository = "source.repo",
                AzureDevOpsBuildNumber = "build.number.2",
                Commit = "sha2",
                DateProduced = DateTimeOffset.UtcNow,
                Assets = new List<Asset> {newAsset}
            };
            var buildChannels = new[]
            {
                new BuildChannel
                {
                    Build = build,
                    Channel = channel
                },
                new BuildChannel
                {
                    Build = newBuild,
                    Channel = channel
                }
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
                    UpdateFrequency = UpdateFrequency.EveryBuild
                },
                LastAppliedBuild = build
            };
            var repoInstallation = new Repository
            {
                RepositoryName = "target.repo",
                InstallationId = 1
            };
            await Context.BuildChannels.AddRangeAsync(buildChannels);
            await Context.Subscriptions.AddAsync(subscription);
            await Context.Repositories.AddAsync(repoInstallation);
            await Context.SaveChangesAsync();

            SubscriptionActor.Verify(a => a.UpdateAsync(build.Id), Times.Never());

            var updater = ActivatorUtilities.CreateInstance<DependencyUpdater>(Scope.ServiceProvider);
            await updater.UpdateDependenciesAsync(newBuild.Id, channel.Id);
        }

        [Test]
        public async Task NoSubscriptions()
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
                DateProduced = DateTimeOffset.UtcNow.AddDays(-1)
            };
            var newBuild = new Build
            {
                AzureDevOpsBranch = "source.branch",
                AzureDevOpsRepository = "source.repo",
                AzureDevOpsBuildNumber = "build.number.2",
                Commit = "sha2",
                DateProduced = DateTimeOffset.UtcNow
            };
            var buildChannels = new[]
            {
                new BuildChannel
                {
                    Build = build,
                    Channel = channel
                },
                new BuildChannel
                {
                    Build = newBuild,
                    Channel = channel
                }
            };
            await Context.BuildChannels.AddRangeAsync(buildChannels);
            await Context.SaveChangesAsync();

            var updater = ActivatorUtilities.CreateInstance<DependencyUpdater>(Scope.ServiceProvider);
            await updater.UpdateDependenciesAsync(newBuild.Id, channel.Id);
        }
    }
}
