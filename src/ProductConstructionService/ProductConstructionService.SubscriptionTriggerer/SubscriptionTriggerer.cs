// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.SubscriptionTriggerer;

public class SubscriptionTriggerer
{
    private readonly IBasicBarClient _barClient;
    private readonly ILogger<SubscriptionTriggerer> _logger;
    private readonly BuildAssetRegistryContext _context;
    private readonly QueueClient _queueClient;

    public SubscriptionTriggerer(
        ILogger<SubscriptionTriggerer> logger,
        BuildAssetRegistryContext context,
        IBasicBarClient barClient,
        QueueClient queueClient)
    {
        _logger = logger;
        _context = context;
        _barClient = barClient;
        _queueClient = queueClient;
    }

    public async Task CheckSubscriptionsAsync(UpdateFrequency targetUpdateFrequency)
    {
        var enabledSubscriptionsWithTargetFrequency = (await _context.Subscriptions
                .Where(s => s.Enabled)
                .ToListAsync())
                .Where(s => s.PolicyObject?.UpdateFrequency == targetUpdateFrequency);

        int subscriptionsUpdated = 0;
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
                _logger.LogInformation("Will trigger {subscriptionId} to build {latestBuildInTargetChannelId}", subscription.Id, latestBuildInTargetChannel.Id);
                UpdateSubscriptionAsync(subscription.Id, latestBuildInTargetChannel.Id);
                subscriptionsUpdated++;
            }
        }
    }

    private void UpdateSubscriptionAsync(Guid subscriptionId, int buildId)
    {
        // TODO https://github.com/dotnet/arcade-services/issues/3802 add item to queue so the subscription gets triggered
        _logger.LogInformation("Queued update for subscription '{subscriptionId}' with build '{buildId}'",
                subscriptionId,
                buildId);
    }
}
