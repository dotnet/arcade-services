// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.Common.FeatureFlags;

/// <summary>
/// Service for managing feature flags stored in Redis.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Sets a feature flag for a specific subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="flag">The feature flag.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="expiry">Optional expiry time for the flag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    Task<FeatureFlagResponse> SetFlagAsync(
        Guid subscriptionId,
        FeatureFlag flag,
        string value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines asynchronously whether the specified feature flag is enabled for a given subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="flag">The feature flag.</param>
    Task<bool> IsFeatureOnAsync(
        Guid subscriptionId,
        FeatureFlag flag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific feature flag for a subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="flag">The feature flag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The feature flag if it exists, null otherwise.</returns>
    Task<FeatureFlagValue?> GetFlagAsync(
        Guid subscriptionId,
        FeatureFlag flag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all feature flags for a specific subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All feature flags for the subscription.</returns>
    Task<IReadOnlyList<FeatureFlagValue>> GetFlagsAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a feature flag for a specific subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="flag">The feature flag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the flag was removed, false if it didn't exist.</returns>
    Task<bool> RemoveFlagAsync(
        Guid subscriptionId,
        FeatureFlag flag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all feature flags across all subscriptions (admin operation).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All feature flags in the system.</returns>
    Task<IReadOnlyList<FeatureFlagValue>> GetAllFlagsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all subscriptions that have a specific feature flag set.
    /// </summary>
    /// <param name="flag">The feature flag to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All subscriptions that have this flag set.</returns>
    Task<IReadOnlyList<FeatureFlagValue>> GetSubscriptionsWithFlagAsync(
        FeatureFlag flag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a specific feature flag from all subscriptions (admin operation).
    /// </summary>
    /// <param name="flag">The feature flag to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of flags removed.</returns>
    Task<int> RemoveFlagFromAllSubscriptionsAsync(
        FeatureFlag flag,
        CancellationToken cancellationToken = default);
}
