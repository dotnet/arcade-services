// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace ProductConstructionService.Common;

/// <summary>
/// Example integration showing how to use feature flags in PCS components.
/// This is a demonstration of how existing components like PullRequestUpdater
/// can leverage feature flags for gradual rollouts and A/B testing.
/// </summary>
public class FeatureFlagIntegrationExample
{
    private readonly IFeatureFlagClient _featureFlagClient;
    private readonly ILogger<FeatureFlagIntegrationExample> _logger;

    public FeatureFlagIntegrationExample(
        IFeatureFlagClient featureFlagClient,
        ILogger<FeatureFlagIntegrationExample> logger)
    {
        _featureFlagClient = featureFlagClient;
        _logger = logger;
    }

    /// <summary>
    /// Example of how PullRequestUpdater could use feature flags to control conflict resolution strategy.
    /// </summary>
    /// <param name="subscriptionId">The subscription being processed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> ProcessPullRequestUpdateAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        // Initialize feature flag client for this subscription
        await _featureFlagClient.InitializeAsync(subscriptionId, cancellationToken);

        // Check if rebase strategy is enabled for this subscription
        var useRebaseStrategy = await _featureFlagClient.GetBooleanFlagAsync(
            FeatureFlags.EnableRebaseStrategy,
            defaultValue: false);

        if (useRebaseStrategy)
        {
            _logger.LogInformation("Using rebase strategy for conflict resolution in subscription {SubscriptionId}", subscriptionId);
            return "Rebase strategy enabled for conflict resolution.";
        }
        else
        {
            _logger.LogInformation("Using standard merge strategy for subscription {SubscriptionId}", subscriptionId);
            return "Standard merge processing.";
        }
    }

    /// <summary>
    /// Example of getting the conflict resolution strategy for a subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription being processed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if rebase strategy should be used, false for merge strategy.</returns>
    public async Task<bool> GetConflictResolutionStrategyAsync(
        Guid subscriptionId, 
        CancellationToken cancellationToken = default)
    {
        await _featureFlagClient.InitializeAsync(subscriptionId, cancellationToken);

        var useRebaseStrategy = await _featureFlagClient.GetBooleanFlagAsync(
            FeatureFlags.EnableRebaseStrategy,
            defaultValue: false);

        _logger.LogDebug("Subscription {SubscriptionId} conflict resolution strategy: {Strategy}",
            subscriptionId, useRebaseStrategy ? "Rebase" : "Merge");

        return useRebaseStrategy;
    }
}