// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;

namespace DependencyUpdater
{
    [DataContract]
    public class DependencyUpdateItem
    {
        [DataMember]
        public int BuildId { get; set; }

        [DataMember]
        public int ChannelId { get; set; }
    }

    /// <summary>
    ///     An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    public sealed class DependencyUpdater : IServiceImplementation, IDependencyUpdater
    {
        private readonly OperationManager _operations;

        public DependencyUpdater(
            IReliableStateManager stateManager,
            ILogger<DependencyUpdater> logger,
            BuildAssetRegistryContext context,
            IRemoteFactory factory,
            IActorProxyFactory<ISubscriptionActor> subscriptionActorFactory,
            OperationManager operations)
        {
            _operations = operations;
            StateManager = stateManager;
            Logger = logger;
            Context = context;
            RemoteFactory = factory;
            SubscriptionActorFactory = subscriptionActorFactory;
        }

        public IReliableStateManager StateManager { get; }
        public ILogger<DependencyUpdater> Logger { get; }
        public BuildAssetRegistryContext Context { get; }
        public IRemoteFactory RemoteFactory { get; }
        public IActorProxyFactory<ISubscriptionActor> SubscriptionActorFactory { get; }

        public async Task StartUpdateDependenciesAsync(int buildId, int channelId)
        {
            IReliableConcurrentQueue<DependencyUpdateItem> queue =
                await StateManager.GetOrAddAsync<IReliableConcurrentQueue<DependencyUpdateItem>>("queue");
            using (ITransaction tx = StateManager.CreateTransaction())
            {
                await queue.EnqueueAsync(
                    tx,
                    new DependencyUpdateItem
                    {
                        BuildId = buildId,
                        ChannelId = channelId
                    });
                await tx.CommitAsync();
            }
        }

        /// <summary>
        ///     Run a single subscription
        /// </summary>
        /// <param name="subscriptionId">Subscription to run the update for.</param>
        /// <returns></returns>
        public Task StartSubscriptionUpdateAsync(Guid subscriptionId)
        {
            var subscriptionToUpdate = (from sub in Context.Subscriptions
                                         where sub.Id == subscriptionId
                                         where sub.Enabled
                                         let latestBuild =
                                             sub.Channel.BuildChannels.Select(bc => bc.Build)
                                                 .Where(b => (sub.SourceRepository == b.GitHubRepository || sub.SourceRepository == b.AzureDevOpsRepository))
                                                 .OrderByDescending(b => b.DateProduced)
                                                 .FirstOrDefault()
                                         where latestBuild != null
                                         where sub.LastAppliedBuildId == null || sub.LastAppliedBuildId != latestBuild.Id
                                         select new
                                         {
                                             subscription = sub.Id,
                                             latestBuild = latestBuild.Id
                                         }).SingleOrDefault();

            if (subscriptionToUpdate != null)
            {
                return UpdateSubscriptionAsync(subscriptionToUpdate.subscription, subscriptionToUpdate.latestBuild);
            }
            return Task.CompletedTask;
        }

        public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            IReliableConcurrentQueue<DependencyUpdateItem> queue =
                await StateManager.GetOrAddAsync<IReliableConcurrentQueue<DependencyUpdateItem>>("queue");

            try
            {
                using (ITransaction tx = StateManager.CreateTransaction())
                {
                    ConditionalValue<DependencyUpdateItem> maybeItem = await queue.TryDequeueAsync(
                        tx,
                        cancellationToken);
                    if (maybeItem.HasValue)
                    {
                        DependencyUpdateItem item = maybeItem.Value;
                        using (_operations.BeginOperation(
                            "Processing dependency update for build {buildId} in channel {channelId}",
                            item.BuildId,
                            item.ChannelId))
                        {
                            await UpdateDependenciesAsync(item.BuildId, item.ChannelId);
                        }
                    }

                    await tx.CommitAsync();
                }
            }
            catch (TaskCanceledException tcex) when (tcex.CancellationToken == cancellationToken)
            {
                return TimeSpan.MaxValue;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Processing queue messages");
            }
            
            return TimeSpan.FromSeconds(1);
        }

        /// <summary>
        ///     Check "EveryDay" subscriptions every day at 5 AM
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [CronSchedule("0 0 5 1/1 * ? *", TimeZones.PST)]
        public async Task CheckDailySubscriptionsAsync(CancellationToken cancellationToken)
        {
            await CheckSubscriptionsAsync(UpdateFrequency.EveryDay, cancellationToken);
        }
        
        /// <summary>
        ///     Check "TwiceDaily" subscriptions at 5 AM and 7 PM
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [CronSchedule("0 0 5,19 * * ?", TimeZones.PST)]
        public async Task CheckTwiceDailySubscriptionsAsync(CancellationToken cancellationToken)
        {
            await CheckSubscriptionsAsync(UpdateFrequency.TwiceDaily, cancellationToken);
        }
        
        /// <summary>
        ///     Check "EveryWeek" subscriptions on Monday at 5 AM
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [CronSchedule("0 0 5 ? * MON", TimeZones.PST)]
        public async Task CheckWeeklySubscriptionsAsync(CancellationToken cancellationToken)
        {
            await CheckSubscriptionsAsync(UpdateFrequency.EveryWeek, cancellationToken);
        }

        private async Task CheckSubscriptionsAsync(UpdateFrequency targetUpdateFrequency, CancellationToken cancellationToken)
        {
            using (_operations.BeginOperation($"Updating {targetUpdateFrequency} subscriptions"))
            {
                var subscriptionsToUpdate = from sub in Context.Subscriptions
                                            where sub.Enabled
                                            let updateFrequency = JsonExtensions.JsonValue(sub.PolicyString, "lax $.UpdateFrequency")
                                            where updateFrequency == ((int)targetUpdateFrequency).ToString()
                                            let latestBuild =
                                                sub.Channel.BuildChannels.Select(bc => bc.Build)
                                                    .Where(b => (sub.SourceRepository == b.GitHubRepository || sub.SourceRepository == b.AzureDevOpsRepository))
                                                    .OrderByDescending(b => b.DateProduced)
                                                    .FirstOrDefault()
                                            where latestBuild != null
                                            where sub.LastAppliedBuildId == null || sub.LastAppliedBuildId != latestBuild.Id
                                            select new
                                            {
                                                subscription = sub.Id,
                                                latestBuild = latestBuild.Id
                                            };

                var subscriptionsAndBuilds = await subscriptionsToUpdate.ToListAsync(cancellationToken);
                Logger.LogInformation($"Will update '{subscriptionsAndBuilds.Count}' subscriptions");

                foreach (var s in subscriptionsAndBuilds)
                {
                    Logger.LogInformation($"Will update {s.subscription} to build {s.latestBuild}");
                    await UpdateSubscriptionAsync(s.subscription, s.latestBuild);
                }
            }
        }

        [CronSchedule("0 0 0 1/1 * ? *", TimeZones.PST)]
        public async Task UpdateLongestBuildPathAsync(CancellationToken cancellationToken)
        {
            using (_operations.BeginOperation($"Updating Longest Build Path table"))
            {
                List<Channel> channels = Context.Channels.Select(c => new Channel() { Id = c.Id, Name = c.Name }).ToList();

                // Get the flow graph
                IRemote barOnlyRemote = await RemoteFactory.GetBarOnlyRemoteAsync(Logger);

                List<Microsoft.DotNet.Maestro.Client.Models.DefaultChannel> defaultChannels = (await barOnlyRemote.GetDefaultChannelsAsync()).ToList();
                List<Microsoft.DotNet.Maestro.Client.Models.Subscription> subscriptions = (await barOnlyRemote.GetSubscriptionsAsync()).ToList();

                IEnumerable<string> frequencies = new[] { "everyWeek", "twiceDaily", "everyDay", "everyBuild", "none", };

                Logger.LogInformation($"Will update '{channels.Count}' channels");

                foreach (var channel in channels)
                {
                    // Build, then prune out what we don't want to see if the user specified channels.
                    DependencyFlowGraph flowGraph = await DependencyFlowGraph.BuildAsync(defaultChannels, subscriptions, barOnlyRemote, 30);

                    flowGraph.PruneGraph(
                        node => DependencyFlowGraph.IsInterestingNode(channel.Name, node), 
                        edge => DependencyFlowGraph.IsInterestingEdge(edge, false, frequencies));

                    if (flowGraph.Nodes.Count > 0)
                    {
                        var edgesWithLastBuild = flowGraph.Edges
                            .Where(e => e.Subscription.LastAppliedBuild != null);

                        foreach (var edge in edgesWithLastBuild)
                        {
                            edge.IsToolingOnly = !Context.IsProductDependency(
                                edge.Subscription.LastAppliedBuild.Id,
                                edge.To.Repository,
                                edge.To.Branch);
                        }

                        flowGraph.MarkBackEdges();
                        flowGraph.CalculateLongestBuildPaths();
                        flowGraph.MarkLongestBuildPath();

                        // Get the nodes on the longest path and order them by path time so that the
                        // contributing repos are in the right order
                        List<DependencyFlowNode> longestBuildPathNodes = flowGraph.Nodes
                            .Where(n => n.OnLongestBuildPath)
                            .OrderByDescending(n => n.BestCasePathTime)
                            .ToList();

                        LongestBuildPath lbp = new LongestBuildPath()
                        {
                            ChannelId = channel.Id,
                            BestCaseTimeInMinutes = longestBuildPathNodes.Max(n => n.BestCasePathTime),
                            WorstCaseTimeInMinutes = longestBuildPathNodes.Max(n => n.WorstCasePathTime),
                            ContributingRepositories = String.Join(';', longestBuildPathNodes.Select(n => $"{n.Repository}@{n.Branch}").ToArray()),
                            ReportDate = DateTimeOffset.UtcNow,
                        };

                        Logger.LogInformation($"Will update {channel.Name} to best case time {lbp.BestCaseTimeInMinutes} and worst case time {lbp.WorstCaseTimeInMinutes}");
                        await Context.LongestBuildPaths.AddAsync(lbp);
                    }
                }

                await Context.SaveChangesAsync();
            }
        }

        /// <summary>
        ///     Update dependencies for a new build in a channel
        /// </summary>
        /// <param name="buildId"></param>
        /// <param name="channelId"></param>
        /// <returns></returns>
        public async Task UpdateDependenciesAsync(int buildId, int channelId)
        {
            Build build = await Context.Builds.FindAsync(buildId);
            List<Subscription> subscriptionsToUpdate = await (from sub in Context.Subscriptions
                where sub.Enabled
                where sub.ChannelId == channelId
                where (sub.SourceRepository == build.GitHubRepository || sub.SourceRepository == build.AzureDevOpsRepository)
                let updateFrequency = JsonExtensions.JsonValue(sub.PolicyString, "lax $.UpdateFrequency")
                where updateFrequency == ((int) UpdateFrequency.EveryBuild).ToString()
                select sub).ToListAsync();
            if (!subscriptionsToUpdate.Any())
            {
                return;
            }

            await Task.WhenAll(subscriptionsToUpdate.Select(sub => UpdateSubscriptionAsync(sub.Id, buildId)));
        }

        private async Task UpdateSubscriptionAsync(Guid subscriptionId, int buildId)
        {
            using (_operations.BeginOperation(
                "Updating subscription '{subscriptionId}' with build '{buildId}'",
                subscriptionId,
                buildId))
            {
                try
                {
                    ISubscriptionActor actor = SubscriptionActorFactory.Lookup(new ActorId(subscriptionId));
                    await actor.UpdateAsync(buildId);
                }
                catch (Exception e)
                {
                    Logger.LogError(e, $"Failed to update subscription '{subscriptionId}' with build '{buildId}'");
                }
            }
        }
    }
}
