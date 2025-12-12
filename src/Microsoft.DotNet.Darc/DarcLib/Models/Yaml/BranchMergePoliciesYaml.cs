// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class BranchMergePoliciesYaml :
    IComparable<BranchMergePoliciesYaml>
{
    public const string RepoElement = "Repository URL";
    public const string BranchElement = "Branch";
    public const string MergePolicyElement = "Merge Policies";

    [YamlIgnore]
    public (string Repository, string Branch) UniqueId => (Repository, Branch);

    [YamlMember(Alias = BranchElement, ApplyNamingConventions = false)]
    public required string Branch { get; init; }

    [YamlMember(Alias = RepoElement, ApplyNamingConventions = false)]
    public required string Repository { get; init; }

    [YamlMember(Alias = MergePolicyElement, ApplyNamingConventions = false)]
    public List<MergePolicyYaml> MergePolicies { get; set; } = [];

    public static BranchMergePoliciesYaml FromClientModel(RepositoryBranch repositoryBranch) => new()
    {
        Repository = repositoryBranch.Repository,
        Branch = repositoryBranch.Branch,
        MergePolicies = MergePolicyYaml.FromClientModels(repositoryBranch.MergePolicies),
    };

    /// <summary>
    /// Compares repository branches for sorting purposes.
    /// Order: Branch (main, master, release/*, internal/release/*, then alphabetically)
    /// </summary>
    public int CompareTo(BranchMergePoliciesYaml? other)
    {
        if (other is null) return 1;

        return BranchOrderComparer.Compare(Branch, other.Branch);
    }
}
