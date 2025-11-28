// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Triggers;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Configuration;

internal static class SubscriptionTriggerConfiguration
{
    public static void TriggerSubscriptionOnNewBuild(IInsertedEntry<BuildChannel, DbContext> entry)
    {
        var context = (BuildAssetRegistryContext)entry.Context;
        ILogger<BuildAssetRegistryContext> logger = context.GetService<ILogger<BuildAssetRegistryContext>>();
        var workItemProducerFactory = context.GetService<IWorkItemProducerFactory>();
        BuildChannel entity = entry.Entity;

        logger.LogInformation("Build {buildId} was added to channel {channelId}. Triggering corresponding subscriptions.",
            entity.BuildId,
            entity.ChannelId);

        Build? build = context.Builds
            .Include(b => b.Assets)
            .ThenInclude(a => a.Locations)
            .FirstOrDefault(b => b.Id == entity.BuildId);

        if (build == null)
        {
            logger.LogError("Could not find build with id {buildId} in BAR. Skipping dependency update.", entity.BuildId);
        }
        else
        {
            var hasAssetsWithPublishedLocations = build.Assets
                .Any(a => a.Locations.Any(al => al.Type != LocationType.None && !al.Location.EndsWith("/artifacts")));

            var subscriptionsToUpdate = context.Subscriptions
                .Where(sub =>
                    sub.Enabled &&
                    sub.ChannelId == entity.ChannelId &&
                    (sub.SourceRepository == entity.Build.GitHubRepository || sub.SourceRepository == entity.Build.AzureDevOpsRepository) &&
                    JsonExtensions.JsonValue(sub.PolicyString, "lax $.UpdateFrequency") == ((int)UpdateFrequency.EveryBuild).ToString())
                .ToList();

            if (subscriptionsToUpdate.Count == 0)
            {
                logger.LogInformation("No subscriptions to trigger for build {buildId}", entity.BuildId);
                return;
            }

            foreach (Subscription subscription in subscriptionsToUpdate)
            {
                if (!hasAssetsWithPublishedLocations && !subscription.SourceEnabled)
                {
                    logger.LogInformation("Skipping subscription {subscriptionId} triggering for Build {buildId} because the build has no assets and the subscription is not source enabled",
                        subscription.Id,
                        entity.BuildId);
                    continue;
                }

                logger.LogInformation("Triggering subscription {subscriptionId} with build {buildId}",
                    subscription.Id,
                    entity.BuildId);

                var workItemProducer = workItemProducerFactory.CreateProducer<SubscriptionTriggerWorkItem>(subscription.SourceEnabled);
                workItemProducer.ProduceWorkItemAsync(new()
                {
                    BuildId = entity.BuildId,
                    SubscriptionId = subscription.Id
                }).GetAwaiter().GetResult();
            }
        }
    }
}
