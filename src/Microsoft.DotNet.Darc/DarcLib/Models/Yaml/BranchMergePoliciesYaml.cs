// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

public class BranchMergePoliciesYaml
{
    public const string RepoElement = "Repository URL";
    public const string BranchElement = "Branch";
    public const string MergePolicyElement = "Merge Policies";

    [YamlMember(Alias = BranchElement, ApplyNamingConventions = false)]
    public required string Branch { get; init; }

    [YamlMember(Alias = RepoElement, ApplyNamingConventions = false)]
    public required string Repository { get; init; }

    [YamlMember(Alias = MergePolicyElement, ApplyNamingConventions = false)]
    public List<MergePolicyYaml> MergePolicies { get; set; } = []
}
