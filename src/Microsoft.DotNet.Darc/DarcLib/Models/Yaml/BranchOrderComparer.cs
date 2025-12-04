// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

/// <summary>
/// Provides branch comparison logic for sorting branches in a specific order:
/// main, master, release/*, internal/release/*, then alphabetically.
/// </summary>
public static class BranchOrderComparer
{
    /// <summary>
    /// Branch patterns in priority order. Exact matches and prefix matches are supported.
    /// </summary>
    private static readonly string[] BranchPriorityPatterns =
    [
        "main",
        "master",
        "release/",
        "internal/release/",
    ];

    /// <summary>
    /// Compares two branch names for sorting purposes.
    /// Order: main, master, release/*, internal/release/*, then alphabetically.
    /// </summary>
    public static int Compare(string? branch1, string? branch2)
    {
        if (branch1 is null && branch2 is null) return 0;
        if (branch1 is null) return 1;
        if (branch2 is null) return -1;

        int priority1 = GetBranchPriority(branch1);
        int priority2 = GetBranchPriority(branch2);

        if (priority1 != priority2)
        {
            return priority1.CompareTo(priority2);
        }

        // Within the same priority group, sort alphabetically
        return string.Compare(branch1, branch2, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetBranchPriority(string branch)
    {
        for (int i = 0; i < BranchPriorityPatterns.Length; i++)
        {
            string pattern = BranchPriorityPatterns[i];

            // Check for exact match or prefix match (patterns ending with "/" are prefixes)
            if (pattern.EndsWith('/'))
            {
                if (branch.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            else if (string.Equals(branch, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return BranchPriorityPatterns.Length;
    }
}
