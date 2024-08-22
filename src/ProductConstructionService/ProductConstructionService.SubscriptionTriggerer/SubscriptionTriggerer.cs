// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.SubscriptionTriggerer;

public class SubscriptionTriggerer
{
    private readonly ILogger<SubscriptionTriggerer> _logger;
    private readonly BuildAssetRegistryContext _context;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;

    public SubscriptionTriggerer(
        ILogger<SubscriptionTriggerer> logger,
        BuildAssetRegistryContext context,
        IWorkItemProducerFactory workItemProducerFactory)
    {
        _logger = logger;
        _context = context;
        _workItemProducerFactory = workItemProducerFactory;
    }

    public async Task CheckSubscriptionsAsync(UpdateFrequency targetUpdateFrequency)
    {
        var enabledSubscriptionsWithTargetFrequency = (await _context.Subscriptions
                .Where(s => s.Enabled)
                .ToListAsync())
                .Where(s => s.PolicyObject?.UpdateFrequency == targetUpdateFrequency);

        var workItemProducer =
            _workItemProducerFactory.CreateClient<UpdateSubscriptionWorkItem>();
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
                // TODO https://github.com/dotnet/arcade-services/issues/3811 add some kind of feature switch to trigger specific subscriptions
                /*await _workItemProducerFactory.Create<UpdateSubscriptionWorkItem>().ProduceWorkItemAsync(new()
                {
                    BuildId = latestBuildInTargetChannel.Id,
                    SubscriptionId = subscription.Id
                });*/
                _logger.LogInformation("Queued update for subscription '{subscriptionId}' with build '{buildId}'",
                    subscription.Id,
                    latestBuildInTargetChannel.Id);
            }
        }
    }
}
