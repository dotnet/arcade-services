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
    /// Example of how PullRequestUpdater could use feature flags to control behavior.
    /// </summary>
    /// <param name="subscriptionId">The subscription being processed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> ProcessPullRequestUpdateAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        // Initialize feature flag client for this subscription
        await _featureFlagClient.InitializeAsync(subscriptionId, cancellationToken);

        // Check if enhanced PR updates are enabled for this subscription
        var enhancedPrUpdates = await _featureFlagClient.GetBooleanFlagAsync(
            FeatureFlags.EnableEnhancedPrUpdates,
            defaultValue: false);

        // Check if batch dependency updates are enabled
        var batchUpdates = await _featureFlagClient.GetBooleanFlagAsync(
            FeatureFlags.EnableBatchDependencyUpdates,
            defaultValue: false);

        // Check if detailed telemetry is enabled
        var detailedTelemetry = await _featureFlagClient.GetBooleanFlagAsync(
            FeatureFlags.EnableDetailedTelemetry,
            defaultValue: false);

        var result = "";

        if (enhancedPrUpdates)
        {
            _logger.LogInformation("Using enhanced PR updates for subscription {SubscriptionId}", subscriptionId);
            result += "Enhanced PR processing enabled. ";
            
            // Enhanced processing would include additional validation, 
            // conflict detection, or improved commit messages
        }
        else
        {
            _logger.LogInformation("Using standard PR updates for subscription {SubscriptionId}", subscriptionId);
            result += "Standard PR processing. ";
        }

        if (batchUpdates)
        {
            _logger.LogInformation("Using batch dependency updates for subscription {SubscriptionId}", subscriptionId);
            result += "Batch processing enabled. ";
            
            // Batch processing would group multiple dependency updates
            // into fewer PRs for better performance
        }

        if (detailedTelemetry)
        {
            _logger.LogInformation("Detailed telemetry enabled for subscription {SubscriptionId}", subscriptionId);
            result += "Detailed telemetry enabled. ";
            
            // Additional telemetry collection for monitoring and analytics
        }

        return result.TrimEnd();
    }

    /// <summary>
    /// Example of subscription-specific configuration using feature flags.
    /// </summary>
    /// <param name="subscriptionId">The subscription being processed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<(bool UseAdvancedMergeResolution, bool UseExperimentalFlow)> GetSubscriptionConfigAsync(
        Guid subscriptionId, 
        CancellationToken cancellationToken = default)
    {
        await _featureFlagClient.InitializeAsync(subscriptionId, cancellationToken);

        var advancedMerge = await _featureFlagClient.GetBooleanFlagAsync(
            FeatureFlags.EnableAdvancedMergeConflictResolution,
            defaultValue: false);

        var experimentalFlow = await _featureFlagClient.GetBooleanFlagAsync(
            FeatureFlags.EnableExperimentalDependencyFlow,
            defaultValue: false);

        _logger.LogDebug("Subscription {SubscriptionId} configuration: Advanced merge={AdvancedMerge}, Experimental flow={ExperimentalFlow}",
            subscriptionId, advancedMerge, experimentalFlow);

        return (advancedMerge, experimentalFlow);
    }
}