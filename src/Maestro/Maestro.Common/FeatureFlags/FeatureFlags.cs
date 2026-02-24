// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.Common.FeatureFlags;

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
    /// Determines whether the specified flag name is a valid, registered feature flag.
    /// </summary>
    /// <param name="flagName">The feature flag key to validate.</param>
    /// <returns>True if the flag is valid, false otherwise.</returns>
    public static bool IsValidFlag(string flagName)
    {
        return !string.IsNullOrEmpty(flagName) && AllFlags.Any(f => f.Name == flagName);
    }

    public static FeatureFlag? GetByName(string flagName) => AllFlags.FirstOrDefault(f => f.Name == flagName);

    public static IEnumerable<string> FlagNames => AllFlags.Select(f => f.Name);
}
