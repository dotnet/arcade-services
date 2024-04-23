// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

namespace DependencyUpdater;

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
    private readonly IBasicBarClient _barClient;
    private readonly OperationManager _operations;
    private readonly IReliableStateManager _stateManager;
    private readonly ILogger<DependencyUpdater> _logger;
    private readonly BuildAssetRegistryContext _context;
    private readonly IActorProxyFactory<ISubscriptionActor> _subscriptionActorFactory;

    public DependencyUpdater(
        IReliableStateManager stateManager,
        ILogger<DependencyUpdater> logger,
        BuildAssetRegistryContext context,
        IBasicBarClient barClient,
        IActorProxyFactory<ISubscriptionActor> subscriptionActorFactory,
        OperationManager operations)
    {
        _operations = operations;
        _stateManager = stateManager;
        _logger = logger;
        _context = context;
        _barClient = barClient;
        _subscriptionActorFactory = subscriptionActorFactory;
    }

    public async Task StartUpdateDependenciesAsync(int buildId, int channelId)
    {
        IReliableConcurrentQueue<DependencyUpdateItem> queue =
            await _stateManager.GetOrAddAsync<IReliableConcurrentQueue<DependencyUpdateItem>>("queue");
        using (ITransaction tx = _stateManager.CreateTransaction())
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
    ///     Run a single subscription, only accept the build Id specified 
    /// </summary>
    /// <param name="subscriptionId">Subscription to run the update for.</param>
    /// <param name="buildId">BAR build id to run the update for</param>
    public Task StartSubscriptionUpdateForSpecificBuildAsync(Guid subscriptionId, int buildId)
    {
        var subscriptionToUpdate = 
            (from sub in _context.Subscriptions
                where sub.Id == subscriptionId
                where sub.Enabled
                let specificBuild =
                    sub.Channel.BuildChannels.Select(bc => bc.Build)
                        .Where(b => (sub.SourceRepository == b.GitHubRepository || sub.SourceRepository == b.AzureDevOpsRepository))
                        .Where(b => b.Id == buildId)
                        .FirstOrDefault()
                where specificBuild != null
                select new
                {
                    subscription = sub.Id,
                    specificBuild = specificBuild.Id
                }).SingleOrDefault();

        if (subscriptionToUpdate != null)
        {
            return UpdateSubscriptionAsync(subscriptionToUpdate.subscription, subscriptionToUpdate.specificBuild);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Run a single subscription, adopting the latest build's id
    /// </summary>
    /// <param name="subscriptionId">Subscription to run the update for.</param>
    /// 
    /// <returns></returns>
    public Task StartSubscriptionUpdateAsync(Guid subscriptionId)
    {
        var subscriptionToUpdate = 
            (from sub in _context.Subscriptions
                where sub.Id == subscriptionId
                where sub.Enabled
                let latestBuild =
                    sub.Channel.BuildChannels.Select(bc => bc.Build)
                        .Where(b => (sub.SourceRepository == b.GitHubRepository || sub.SourceRepository == b.AzureDevOpsRepository))
                        .OrderByDescending(b => b.DateProduced)
                        .FirstOrDefault()
                where latestBuild != null
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
            await _stateManager.GetOrAddAsync<IReliableConcurrentQueue<DependencyUpdateItem>>("queue");

        try
        {
            using (ITransaction tx = _stateManager.CreateTransaction())
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
            _logger.LogError(ex, "Processing queue messages");
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
            var enabledSubscriptionsWithTargetFrequency = (await _context.Subscriptions
                    .Where(s => s.Enabled)
                    .ToListAsync(cancellationToken))
                    .Where(s => s.PolicyObject?.UpdateFrequency == targetUpdateFrequency);

            int subscriptionsUpdated = 0;
            foreach (var subscription in enabledSubscriptionsWithTargetFrequency)
            {
                Subscription subscriptionWithBuilds = await _context.Subscriptions
                    .Where(s => s.Id == subscription.Id)
                    .Include(s => s.Channel)
                    .ThenInclude(c => c.BuildChannels)
                    .ThenInclude(bc => bc.Build)
                    .FirstOrDefaultAsync(cancellationToken);

                if (subscriptionWithBuilds == null)
                {
                    _logger.LogWarning("Subscription {subscriptionId} was not found in the BAR. Not applying updates", subscription.Id.ToString());
                    continue;
                }

                Build latestBuildInTargetChannel = subscriptionWithBuilds.Channel.BuildChannels.Select(bc => bc.Build)
                    .Where(b => (subscription.SourceRepository == b.GitHubRepository || subscription.SourceRepository == b.AzureDevOpsRepository))
                    .OrderByDescending(b => b.DateProduced)
                    .FirstOrDefault();

                bool isThereAnUnappliedBuildInTargetChannel = latestBuildInTargetChannel != null &&
                    (subscription.LastAppliedBuild == null || subscription.LastAppliedBuildId != latestBuildInTargetChannel.Id);

                if (isThereAnUnappliedBuildInTargetChannel)
                {
                    _logger.LogInformation("Will update {subscriptionId} to build {latestBuildInTargetChannelId}", subscription.Id, latestBuildInTargetChannel.Id);
                    await UpdateSubscriptionAsync(subscription.Id, latestBuildInTargetChannel.Id);
                    subscriptionsUpdated++;
                }
            }

            _logger.LogInformation("Updated '{SubscriptionsUpdated}' '{targetUpdateFrequency}' subscriptions", subscriptionsUpdated, targetUpdateFrequency.ToString());
        }
    }

    [CronSchedule("0 0 0 1/1 * ? *", TimeZones.PST)]
    public async Task UpdateLongestBuildPathAsync(CancellationToken cancellationToken)
    {
        using (_operations.BeginOperation($"Updating Longest Build Path table"))
        {
            List<Channel> channels = [.. _context.Channels.Select(c => new Channel() { Id = c.Id, Name = c.Name })];
            IReadOnlyList<string> frequencies = new[] { "everyWeek", "twiceDaily", "everyDay", "everyBuild", "none", };

            _logger.LogInformation($"Will update '{channels.Count}' channels");

            foreach (var channel in channels)
            {
                var flowGraph = await _barClient.GetDependencyFlowGraphAsync(
                    channel.Id,
                    days: 30,
                    includeArcade: false,
                    includeBuildTimes: true,
                    includeDisabledSubscriptions: false,
                    includedFrequencies: frequencies);

                // Get the nodes on the longest path and order them by path time so that the
                // contributing repos are in the right order
                List<DependencyFlowNode> longestBuildPathNodes = [.. flowGraph.Nodes
                    .Where(n => n.OnLongestBuildPath)
                    .OrderByDescending(n => n.BestCasePathTime)];

                if (longestBuildPathNodes.Any())
                {
                    var lbp = new LongestBuildPath()
                    {
                        ChannelId = channel.Id,
                        BestCaseTimeInMinutes = longestBuildPathNodes.Max(n => n.BestCasePathTime),
                        WorstCaseTimeInMinutes = longestBuildPathNodes.Max(n => n.WorstCasePathTime),
                        ContributingRepositories = string.Join(';', longestBuildPathNodes.Select(n => $"{n.Repository}@{n.Branch}").ToArray()),
                        ReportDate = DateTimeOffset.UtcNow,
                    };

                    _logger.LogInformation($"Will update {channel.Name} to best case time {lbp.BestCaseTimeInMinutes} and worst case time {lbp.WorstCaseTimeInMinutes}");
                    await _context.LongestBuildPaths.AddAsync(lbp);
                }
                else
                {
                    _logger.LogInformation($"Will not update {channel.Name} longest build path because no nodes have {nameof(DependencyFlowNode.OnLongestBuildPath)} flag set. Total node count = {flowGraph.Nodes.Count}");
                }
            }

            await _context.SaveChangesAsync();
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
        Build build = await _context.Builds.FindAsync(buildId);
        List<Subscription> subscriptionsToUpdate = await (from sub in _context.Subscriptions
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
                ISubscriptionActor actor = _subscriptionActorFactory.Lookup(new ActorId(subscriptionId));
                await actor.UpdateAsync(buildId);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to update subscription '{subscriptionId}' with build '{buildId}'");
            }
        }
    }
}
