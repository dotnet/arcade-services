// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.MaestroConfiguration.Client.Models;

public record BranchMergePoliciesYaml : IYamlModel
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

    public static BranchMergePoliciesYaml FromClientModel(RepositoryBranch repositoryBranch) => new()
    {
        Repository = repositoryBranch.Repository,
        Branch = repositoryBranch.Branch,
        MergePolicies = MergePolicyYaml.FromClientModels(repositoryBranch.MergePolicies),
    };
}
