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

            if (!hasAssetsWithPublishedLocations)
            {
                logger.LogInformation("Skipping Dependency update for Build {buildId} because it contains no assets in valid locations", entity.BuildId);
                return;
            }

            var subscriptionsToUpdate = context.Subscriptions
                .Where(sub =>
                    sub.Enabled &&
                    sub.ChannelId == entity.ChannelId &&
                    (sub.SourceRepository == entity.Build.GitHubRepository || sub.SourceDirectory == entity.Build.AzureDevOpsRepository) &&
                    JsonExtensions.JsonValue(sub.PolicyString, "lax $.UpdateFrequency") == ((int)UpdateFrequency.EveryBuild).ToString())
                .ToList();

            foreach (Subscription subscription in subscriptionsToUpdate)
            {
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
