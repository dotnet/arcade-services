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
    /// Enables enhanced pull request updates with additional validation and reporting.
    /// </summary>
    public const string EnableEnhancedPrUpdates = "enable-enhanced-pr-updates";

    /// <summary>
    /// Enables batch processing for dependency updates to improve performance.
    /// </summary>
    public const string EnableBatchDependencyUpdates = "enable-batch-dependency-updates";

    /// <summary>
    /// Enables advanced merge conflict resolution strategies.
    /// </summary>
    public const string EnableAdvancedMergeConflictResolution = "enable-advanced-merge-conflict-resolution";

    /// <summary>
    /// Enables detailed telemetry collection for subscription processing.
    /// </summary>
    public const string EnableDetailedTelemetry = "enable-detailed-telemetry";

    /// <summary>
    /// Enables experimental dependency flow optimizations.
    /// </summary>
    public const string EnableExperimentalDependencyFlow = "enable-experimental-dependency-flow";

    /// <summary>
    /// Gets all available feature flags with their metadata.
    /// </summary>
    public static readonly Dictionary<string, FeatureFlagMetadata> AllFlags = new()
    {
        [EnableEnhancedPrUpdates] = new FeatureFlagMetadata(
            EnableEnhancedPrUpdates,
            "Enhanced PR Updates",
            "Enables enhanced pull request updates with additional validation and reporting.",
            FeatureFlagType.Boolean),

        [EnableBatchDependencyUpdates] = new FeatureFlagMetadata(
            EnableBatchDependencyUpdates,
            "Batch Dependency Updates",
            "Enables batch processing for dependency updates to improve performance.",
            FeatureFlagType.Boolean),

        [EnableAdvancedMergeConflictResolution] = new FeatureFlagMetadata(
            EnableAdvancedMergeConflictResolution,
            "Advanced Merge Conflict Resolution",
            "Enables advanced merge conflict resolution strategies.",
            FeatureFlagType.Boolean),

        [EnableDetailedTelemetry] = new FeatureFlagMetadata(
            EnableDetailedTelemetry,
            "Detailed Telemetry",
            "Enables detailed telemetry collection for subscription processing.",
            FeatureFlagType.Boolean),

        [EnableExperimentalDependencyFlow] = new FeatureFlagMetadata(
            EnableExperimentalDependencyFlow,
            "Experimental Dependency Flow",
            "Enables experimental dependency flow optimizations.",
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