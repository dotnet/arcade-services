// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Common;

/// <summary>
/// Defines all available feature flags for the Product Construction Service.
/// Each feature flag has a unique key and metadata describing its purpose.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Enables rebase strategy for codeflow conflict resolution instead of merge.
    /// </summary>
    public const string EnableRebaseStrategy = "enable-rebase-strategy";

    /// <summary>
    /// Gets all available feature flags with their metadata.
    /// </summary>
    public static readonly Dictionary<string, FeatureFlagMetadata> AllFlags = new()
    {
        [EnableRebaseStrategy] = new FeatureFlagMetadata(
            EnableRebaseStrategy,
            "Rebase Strategy",
            "Enables rebase strategy for codeflow conflict resolution instead of merge.",
            FeatureFlagType.Boolean)
    };

    /// <summary>
    /// Validates if a feature flag key is recognized.
    /// </summary>
    /// <param name="flagKey">The feature flag key to validate.</param>
    /// <returns>True if the flag is valid, false otherwise.</returns>
    public static bool IsValidFlag(string flagKey)
    {
        return !string.IsNullOrEmpty(flagKey) && AllFlags.ContainsKey(flagKey);
    }

    /// <summary>
    /// Gets metadata for a specific feature flag.
    /// </summary>
    /// <param name="flagKey">The feature flag key.</param>
    /// <returns>The metadata if the flag exists, null otherwise.</returns>
    public static FeatureFlagMetadata? GetMetadata(string flagKey)
    {
        return AllFlags.TryGetValue(flagKey, out var metadata) ? metadata : null;
    }
}

/// <summary>
/// Represents the type of a feature flag value.
/// </summary>
public enum FeatureFlagType
{
    Boolean,
    String,
    Integer,
    Double
}

/// <summary>
/// Metadata for a feature flag.
/// </summary>
/// <param name="Key">The unique identifier for the feature flag.</param>
/// <param name="Name">The display name of the feature flag.</param>
/// <param name="Description">A description of what the feature flag controls.</param>
/// <param name="Type">The expected type of the feature flag value.</param>
public record FeatureFlagMetadata(
    string Key,
    string Name,
    string Description,
    FeatureFlagType Type);