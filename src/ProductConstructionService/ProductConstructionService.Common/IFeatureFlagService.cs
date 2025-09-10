// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Common;

/// <summary>
/// Service for managing feature flags stored in Redis.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Sets a feature flag for a specific subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="flagName">The name of the feature flag.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="expiry">Optional expiry time for the flag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    Task<FeatureFlagResponse> SetFlagAsync(
        Guid subscriptionId,
        string flagName,
        string value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific feature flag for a subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="flagName">The name of the feature flag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The feature flag if it exists, null otherwise.</returns>
    Task<FeatureFlagValue?> GetFlagAsync(
        Guid subscriptionId,
        string flagName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all feature flags for a specific subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All feature flags for the subscription.</returns>
    Task<IReadOnlyList<FeatureFlagValue>> GetFlagsForSubscriptionAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a feature flag for a specific subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="flagName">The name of the feature flag to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the flag was removed, false if it didn't exist.</returns>
    Task<bool> RemoveFlagAsync(
        Guid subscriptionId,
        string flagName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all feature flags across all subscriptions (admin operation).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All feature flags in the system.</returns>
    Task<IReadOnlyList<FeatureFlagValue>> GetAllFlagsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a feature flag name and value.
    /// </summary>
    /// <param name="flagName">The flag name to validate.</param>
    /// <param name="value">The value to validate.</param>
    /// <returns>Validation result with any error messages.</returns>
    FeatureFlagResponse ValidateFlag(string flagName, string value);
}

/// <summary>
/// Client service for strongly-typed access to feature flags for a specific subscription.
/// This is a scoped service that caches flag values for the lifetime of the scope.
/// </summary>
public interface IFeatureFlagClient
{
    /// <summary>
    /// Initializes the client for a specific subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID to work with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a boolean feature flag value.
    /// </summary>
    /// <param name="flagName">The flag name (should be a constant from FeatureFlags class).</param>
    /// <param name="defaultValue">The default value if the flag is not set.</param>
    /// <returns>The flag value or default.</returns>
    Task<bool> GetBooleanFlagAsync(string flagName, bool defaultValue = false);

    /// <summary>
    /// Gets a string feature flag value.
    /// </summary>
    /// <param name="flagName">The flag name (should be a constant from FeatureFlags class).</param>
    /// <param name="defaultValue">The default value if the flag is not set.</param>
    /// <returns>The flag value or default.</returns>
    Task<string> GetStringFlagAsync(string flagName, string defaultValue = "");

    /// <summary>
    /// Gets an integer feature flag value.
    /// </summary>
    /// <param name="flagName">The flag name (should be a constant from FeatureFlags class).</param>
    /// <param name="defaultValue">The default value if the flag is not set.</param>
    /// <returns>The flag value or default.</returns>
    Task<int> GetIntegerFlagAsync(string flagName, int defaultValue = 0);

    /// <summary>
    /// Gets a double feature flag value.
    /// </summary>
    /// <param name="flagName">The flag name (should be a constant from FeatureFlags class).</param>
    /// <param name="defaultValue">The default value if the flag is not set.</param>
    /// <returns>The flag value or default.</returns>
    Task<double> GetDoubleFlagAsync(string flagName, double defaultValue = 0.0);
}