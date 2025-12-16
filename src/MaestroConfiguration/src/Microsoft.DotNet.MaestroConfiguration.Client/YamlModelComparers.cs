// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

/// <summary>
/// Comparer for <see cref="SubscriptionYaml"/> instances.
/// Order: TargetBranch (main, master, release/*, internal/release/*, then alphabetically), Channel, SourceRepository, Id
/// </summary>
public class SubscriptionYamlComparer : IComparer<SubscriptionYaml>
{
    public int Compare(SubscriptionYaml? x, SubscriptionYaml? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int result = BranchOrderComparer.Compare(x.TargetBranch, y.TargetBranch);
        if (result != 0) return result;

        result = string.Compare(x.Channel, y.Channel, StringComparison.OrdinalIgnoreCase);
        if (result != 0) return result;

        result = string.Compare(x.SourceRepository, y.SourceRepository, StringComparison.OrdinalIgnoreCase);
        if (result != 0) return result;

        return x.Id.CompareTo(y.Id);
    }
}

/// <summary>
/// Comparer for <see cref="DefaultChannelYaml"/> instances.
/// Order: Branch (main, master, release/*, internal/release/*, then alphabetically), Channel
/// </summary>
public class DefaultChannelYamlComparer : IComparer<DefaultChannelYaml>
{
    public int Compare(DefaultChannelYaml? x, DefaultChannelYaml? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int result = BranchOrderComparer.Compare(x.Branch, y.Branch);
        if (result != 0) return result;

        return string.Compare(x.Channel, y.Channel, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Comparer for <see cref="ChannelYaml"/> instances.
/// Order: Name
/// </summary>
public class ChannelYamlComparer : IComparer<ChannelYaml>
{
    public int Compare(ChannelYaml? x, ChannelYaml? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Comparer for <see cref="BranchMergePoliciesYaml"/> instances.
/// Order: Branch (main, master, release/*, internal/release/*, then alphabetically)
/// </summary>
public class BranchMergePoliciesYamlComparer : IComparer<BranchMergePoliciesYaml>
{
    public int Compare(BranchMergePoliciesYaml? x, BranchMergePoliciesYaml? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        return BranchOrderComparer.Compare(x.Branch, y.Branch);
    }
}

/// <summary>
/// Provides branch comparison logic for sorting branches in a specific order:
/// main, master, release/*, internal/release/*, then alphabetically.
/// </summary>
internal static class BranchOrderComparer
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
