// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


namespace ProductConstructionService.Common;

public class FeatureFlag
{
    public static readonly FeatureFlag EnableRebaseStrategy = new("enable-rebase-strategy");

    private FeatureFlag(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

/// <summary>
/// Defines all available feature flags for the Product Construction Service.
/// Each feature flag has a unique key and metadata describing its purpose.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Gets all available feature flags with their metadata.
    /// </summary>
    public static readonly IReadOnlyCollection<FeatureFlag> AllFlags =
    [
        FeatureFlag.EnableRebaseStrategy
    ];

    /// <summary>
    /// Validates if a feature flag key is recognized.
    /// </summary>
    /// <param name="flagKey">The feature flag key to validate.</param>
    /// <returns>True if the flag is valid, false otherwise.</returns>
    public static bool IsValidFlag(string flagKey)
    {
        return !string.IsNullOrEmpty(flagKey) && AllFlags.Any(f => f.Name == flagKey);
    }

    public static FeatureFlag? GetByName(string flagName) => AllFlags.FirstOrDefault(f => f.Name == flagName);

    public static IEnumerable<string> FlagNames => AllFlags.Select(f => f.Name);
}
