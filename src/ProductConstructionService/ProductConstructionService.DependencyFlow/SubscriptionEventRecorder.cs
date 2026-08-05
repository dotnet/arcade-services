// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.DependencyFlow;

public interface ISubscriptionEventRecorder
{
    Task UpdateSubscriptionsForMergedPRAsync(
        IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates);
}

internal class SubscriptionEventRecorder(
    ILogger<ISubscriptionEventRecorder> logger,
    BuildAssetRegistryContext context,
    IRemoteFactory remoteFactory) : ISubscriptionEventRecorder
{
    private readonly ILogger<ISubscriptionEventRecorder> _logger = logger;
    private readonly BuildAssetRegistryContext _context = context;
    private readonly IRemoteFactory _remoteFactory = remoteFactory;

    public async Task UpdateSubscriptionsForMergedPRAsync(IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates)
    {
        _logger.LogInformation("Updating subscriptions for merged PR");
        foreach (SubscriptionPullRequestUpdate update in subscriptionPullRequestUpdates)
        {
            await UpdateForMergedPullRequestAsync(update);
        }
    }

    private async Task UpdateForMergedPullRequestAsync(SubscriptionPullRequestUpdate update)
    {
        _logger.LogInformation("Updating {subscriptionId} with latest build id {buildId}", update.SubscriptionId, update.BuildId);
        Subscription? subscription = await _context.Subscriptions.FindAsync(update.SubscriptionId);

        if (subscription != null)
        {
            subscription.LastAppliedBuildId = subscription.SourceEnabled
                // We must check if the build really got applied or if someone merged an earlier build without resolving conflicts
                ? await GetLastCodeflownBuild(subscription)
                : update.BuildId;
            _context.Subscriptions.Update(subscription);
            await _context.SaveChangesAsync();
        }
        else
        {
            // This happens for deleted subscriptions (such as scenario tests)
            _logger.LogInformation("Could not find subscription with ID {subscriptionId}. Skipping latestBuild update.", update.SubscriptionId);
        }
    }

    private async Task<int> GetLastCodeflownBuild(Subscription subscription)
    {
        var remote = await _remoteFactory.CreateRemoteAsync(subscription.TargetRepository);
        if (!string.IsNullOrEmpty(subscription.SourceDirectory))
        {
            // Backflow
            var sourceTag = await remote.GetSourceDependencyAsync(subscription.TargetRepository, subscription.TargetBranch);

            return sourceTag?.BarId
                ?? throw new DarcException($"Failed to determine last flown VMR build " +
                                           $"to {subscription.TargetRepository} @ {subscription.TargetBranch}");
        }
        else
        {
            // Forward flow
            var sourceManifest = await remote.GetSourceManifestAsync(subscription.TargetRepository, subscription.TargetBranch);

            return sourceManifest.GetRepositoryRecord(subscription.TargetDirectory)?.BarId
                ?? throw new DarcException($"Failed to determine last flown build of {subscription.TargetDirectory} " +
                                           $"to {subscription.TargetRepository} @ {subscription.TargetBranch}");
        }
    }
}
