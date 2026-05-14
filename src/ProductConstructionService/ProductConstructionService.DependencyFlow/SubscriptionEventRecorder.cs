// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow;

public interface ISubscriptionEventRecorder
{
    Task AddDependencyFlowEventsAsync(
        IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates,
        DependencyFlowEventType flowEvent,
        DependencyFlowEventReason reason,
        MergePolicyCheckResult policy,
        string? prUrl);

    Task RegisterSubscriptionUpdateAction(
        SubscriptionUpdateAction subscriptionUpdateAction,
        Guid subscriptionId);

    Task UpdateSubscriptionsForMergedPRAsync(
        IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates);
}

internal class SubscriptionEventRecorder(
    ISqlBarClient sqlClient,
    ILogger<ISubscriptionEventRecorder> logger,
    BuildAssetRegistryContext context,
    IRemoteFactory remoteFactory,
    IPullRequestUpdaterFactory updaterFactory) : ISubscriptionEventRecorder
{
    private readonly ISqlBarClient _sqlClient = sqlClient;
    private readonly ILogger<ISubscriptionEventRecorder> _logger = logger;
    private readonly BuildAssetRegistryContext _context = context;
    private readonly IRemoteFactory _remoteFactory = remoteFactory;
    private readonly IPullRequestUpdaterFactory _updaterFactory = updaterFactory;

    public async Task AddDependencyFlowEventsAsync(IEnumerable<SubscriptionPullRequestUpdate> subscriptionPullRequestUpdates, DependencyFlowEventType flowEvent, DependencyFlowEventReason reason, MergePolicyCheckResult policy, string? prUrl)
    {
        foreach (SubscriptionPullRequestUpdate update in subscriptionPullRequestUpdates)
        {
            ISubscriptionTriggerer triggerer = _updaterFactory.CreateSubscriptionTrigerrer(update.SubscriptionId);
            if (!await triggerer.AddDependencyFlowEventAsync(update.BuildId, flowEvent, reason, policy, "PR", prUrl))
            {
                _logger.LogInformation("Failed to add dependency flow event for {subscriptionId}", update.SubscriptionId);
            }
        }
    }

    public async Task RegisterSubscriptionUpdateAction(SubscriptionUpdateAction subscriptionUpdateAction, Guid subscriptionId)
    {
        string updateMessage = subscriptionUpdateAction.ToString();
        await _sqlClient.RegisterSubscriptionUpdate(subscriptionId, updateMessage);
    }

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
