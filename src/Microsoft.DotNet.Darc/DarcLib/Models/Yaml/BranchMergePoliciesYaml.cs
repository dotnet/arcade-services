// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class BranchMergePoliciesYaml : IComparable<BranchMergePoliciesYaml>
{
    public const string RepoElement = "Repository URL";
    public const string BranchElement = "Branch";
    public const string MergePolicyElement = "Merge Policies";

    [YamlMember(Alias = BranchElement, ApplyNamingConventions = false)]
    public required string Branch { get; init; }

    [YamlMember(Alias = RepoElement, ApplyNamingConventions = false)]
    public required string Repository { get; init; }

    [YamlMember(Alias = MergePolicyElement, ApplyNamingConventions = false)]
    public List<MergePolicyYaml> MergePolicies { get; set; } = [];

    /// <summary>
    /// Compares repository branches for sorting purposes.
    /// Order: Branch
    /// </summary>
    public int CompareTo(BranchMergePoliciesYaml? other)
    {
        if (other is null) return 1;

        return string.Compare(Branch, other.Branch, StringComparison.OrdinalIgnoreCase);
    }

    public static IComparer<BranchMergePoliciesYaml> Comparer { get; } = Comparer<BranchMergePoliciesYaml>.Create((x, y) => x?.CompareTo(y) ?? (y is null ? 0 : -1));
}
