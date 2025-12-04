// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Yaml;

/// <summary>
/// Helper class for YAML encoding/decoding purposes.
/// This is used so that we can have friendly alias names for elements.
/// </summary>
public class SubscriptionYaml
{
    public const string IdElement = "Id";
    public const string EnabledElement = "Enabled";
    public const string ChannelElement = "Channel";
    public const string SourceRepoElement = "Source Repository URL";
    public const string TargetRepoElement = "Target Repository URL";
    public const string TargetBranchElement = "Target Branch";
    public const string UpdateFrequencyElement = "Update Frequency";
    public const string BatchableElement = "Batchable";
    public const string ExcludedAssetsElement = "Excluded Assets";
    public const string MergePolicyElement = "Merge Policies";
    public const string FailureNotificationTagsElement = "Pull Request Failure Notification Tags";
    public const string SourceEnabledElement = "Source Enabled";
    public const string SourceDirectoryElement = "Source Directory";
    public const string TargetDirectoryElement = "Target Directory";

    [YamlMember(Alias = IdElement, ApplyNamingConventions = false)]
    public required Guid Id { get; init; }

    [DefaultValue(true)]
    [YamlMember(Alias = EnabledElement, ApplyNamingConventions = false)]
    public bool Enabled { get; init; } = true;

    [YamlMember(Alias = ChannelElement, ApplyNamingConventions = false)]
    public required string Channel { get; init; }

    [YamlMember(Alias = SourceRepoElement, ApplyNamingConventions = false)]
    public required string SourceRepository { get; init; }

    [YamlMember(Alias = TargetRepoElement, ApplyNamingConventions = false)]
    public required string TargetRepository { get; init; }

    [YamlMember(Alias = TargetBranchElement, ApplyNamingConventions = false)]
    public required string TargetBranch { get; init; }

    [YamlMember(Alias = UpdateFrequencyElement, ApplyNamingConventions = false)]
    public UpdateFrequency UpdateFrequency { get; init; } = UpdateFrequency.None;

    [YamlMember(Alias = BatchableElement, ApplyNamingConventions = false)]
    public bool Batchable { get; set; } = false;

    [YamlMember(Alias = ExcludedAssetsElement, ApplyNamingConventions = false)]
    public List<string> ExcludedAssets { get; init; } = [];

    [YamlMember(Alias = MergePolicyElement, ApplyNamingConventions = false)]
    public List<MergePolicyYaml> MergePolicies { get; init; } = [];

    [YamlMember(Alias = FailureNotificationTagsElement, ApplyNamingConventions = false)]
    public string? FailureNotificationTags { get; set; }

    [YamlMember(Alias = SourceEnabledElement, ApplyNamingConventions = false)]
    public bool SourceEnabled { get; set; } = false;

    [YamlMember(Alias = SourceDirectoryElement, ApplyNamingConventions = false)]
    public string? SourceDirectory { get; set; }

    [YamlMember(Alias = TargetDirectoryElement, ApplyNamingConventions = false)]
    public string? TargetDirectory { get; set; }
}
