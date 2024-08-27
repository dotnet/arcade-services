﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.SubscriptionTriggerer;

public class SubscriptionTriggerer
{
    private readonly ILogger<SubscriptionTriggerer> _logger;
    private readonly BuildAssetRegistryContext _context;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;
    private readonly SubscriptionIdGenerator _subscriptionIdGenerator;

    public SubscriptionTriggerer(
        ILogger<SubscriptionTriggerer> logger,
        BuildAssetRegistryContext context,
        IWorkItemProducerFactory workItemProducerFactory,
        SubscriptionIdGenerator subscriptionIdGenerator)
    {
        _logger = logger;
        _context = context;
        _workItemProducerFactory = workItemProducerFactory;
        _subscriptionIdGenerator = subscriptionIdGenerator;
    }

    public async Task TriggerSubscriptionsAsync(UpdateFrequency targetUpdateFrequency)
    {
        var workItemProducer = _workItemProducerFactory.CreateProducer<SubscriptionUpdateWorkItem>();
        foreach (var updateSubscriptionWorkItem in await GetSubscriptionsToTrigger(targetUpdateFrequency))
        {
            await workItemProducer.ProduceWorkItemAsync(updateSubscriptionWorkItem);
            _logger.LogInformation("Queued update for subscription '{subscriptionId}' with build '{buildId}'",
                    updateSubscriptionWorkItem.SubscriptionId,
                    updateSubscriptionWorkItem.BuildId);
        }
    }

    private async Task<List<SubscriptionUpdateWorkItem>> GetSubscriptionsToTrigger(UpdateFrequency targetUpdateFrequency)
    {
        List<SubscriptionUpdateWorkItem> subscriptionsToTrigger = new();

        var enabledSubscriptionsWithTargetFrequency = (await _context.Subscriptions
                .Where(s => s.Enabled)
                // TODO (https://github.com/dotnet/arcade-services/issues/3880) - Remove subscriptionIdGenerator
                .Where(s => _subscriptionIdGenerator.ShouldTriggerSubscription(s.Id))
                .ToListAsync())
                .Where(s => s.PolicyObject?.UpdateFrequency == targetUpdateFrequency);

        var workItemProducer =
            _workItemProducerFactory.CreateProducer<SubscriptionTriggerWorkItem>();
        foreach (var subscription in enabledSubscriptionsWithTargetFrequency)
        {
            Subscription? subscriptionWithBuilds = await _context.Subscriptions
                .Where(s => s.Id == subscription.Id)
                .Include(s => s.Channel)
                .ThenInclude(c => c.BuildChannels)
                .ThenInclude(bc => bc.Build)
                .FirstOrDefaultAsync();

            if (subscriptionWithBuilds == null)
            {
                _logger.LogWarning("Subscription {subscriptionId} was not found in the BAR. Not triggering updates", subscription.Id.ToString());
                continue;
            }

            Build? latestBuildInTargetChannel = subscriptionWithBuilds.Channel.BuildChannels.Select(bc => bc.Build)
                .Where(b => (subscription.SourceRepository == b.GitHubRepository || subscription.SourceRepository == b.AzureDevOpsRepository))
                .OrderByDescending(b => b.DateProduced)
                .FirstOrDefault();

            bool isThereAnUnappliedBuildInTargetChannel = latestBuildInTargetChannel != null &&
                (subscription.LastAppliedBuild == null || subscription.LastAppliedBuildId != latestBuildInTargetChannel.Id);

            if (isThereAnUnappliedBuildInTargetChannel && latestBuildInTargetChannel != null)
            {
                subscriptionsToTrigger.Add(new SubscriptionUpdateWorkItem
                {
                    BuildId = latestBuildInTargetChannel.Id,
                    SubscriptionId = subscription.Id
                });
            }
        }

        return subscriptionsToTrigger;
    }
}
