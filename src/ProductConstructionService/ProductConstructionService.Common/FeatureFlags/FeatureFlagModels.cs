// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Common.FeatureFlags;

/// <summary>
/// Represents a feature flag value for a specific subscription.
/// </summary>
/// <param name="SubscriptionId">The subscription ID this flag applies to.</param>
/// <param name="FlagName">The name of the feature flag.</param>
/// <param name="Value">The value of the feature flag.</param>
/// <param name="CreatedAt">When the flag was created.</param>
public record FeatureFlagValue(
    Guid SubscriptionId,
    string FlagName,
    string Value,
    DateTimeOffset? CreatedAt = null);

/// <summary>
/// Request model for setting a feature flag.
/// </summary>
/// <param name="SubscriptionId">The subscription ID this flag applies to.</param>
/// <param name="FlagName">The name of the feature flag.</param>
/// <param name="Value">The value to set for the feature flag.</param>
/// <param name="ExpiryDays">The number of days until the flag expires, if any.</param>
public record SetFeatureFlagRequest(
    Guid SubscriptionId,
    string FlagName,
    string Value,
    int? ExpiryDays = null);

/// <summary>
/// Response model for feature flag operations.
/// </summary>
/// <param name="Success">Whether the operation was successful.</param>
/// <param name="Message">Any message associated with the operation.</param>
/// <param name="Flag">The feature flag data, if applicable.</param>
public record FeatureFlagResponse(
    bool Success,
    string? Message = null,
    FeatureFlagValue? Flag = null);

/// <summary>
/// Response model for listing feature flags.
/// </summary>
/// <param name="Flags">The list of feature flags.</param>
/// <param name="Total">The total count of flags.</param>
public record FeatureFlagListResponse(
    IReadOnlyCollection<FeatureFlagValue> Flags,
    int Total);

/// <summary>
/// Response model for available feature flags metadata.
/// </summary>
/// <param name="Flags">The list of available feature flag definitions.</param>
public record AvailableFeatureFlagsResponse(
    IReadOnlyCollection<string> Flags);

/// <summary>
/// Response model for removing a feature flag from all subscriptions.
/// </summary>
/// <param name="RemovedCount">The number of subscriptions from which the flag was removed.</param>
/// <param name="Message">Any message associated with the operation.</param>
public record RemoveFlagFromAllResponse(
    int RemovedCount,
    string? Message = null);
