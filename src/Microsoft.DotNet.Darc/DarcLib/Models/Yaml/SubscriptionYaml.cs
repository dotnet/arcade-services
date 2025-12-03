// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using YamlDotNet.Serialization;

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
    public string Id { get; set; }

    [DefaultValue("True")]
    [YamlMember(Alias = EnabledElement, ApplyNamingConventions = false)]
    public string Enabled { get; set; }

    [YamlMember(Alias = ChannelElement, ApplyNamingConventions = false)]
    public string Channel { get; set; }

    [YamlMember(Alias = SourceRepoElement, ApplyNamingConventions = false)]
    public string SourceRepository { get; set; }

    [YamlMember(Alias = TargetRepoElement, ApplyNamingConventions = false)]
    public string TargetRepository { get; set; }

    [YamlMember(Alias = TargetBranchElement, ApplyNamingConventions = false)]
    public string TargetBranch { get; set; }

    [YamlMember(Alias = UpdateFrequencyElement, ApplyNamingConventions = false)]
    public string UpdateFrequency { get; set; }

    [DefaultValue("False")]
    [YamlMember(Alias = BatchableElement, ApplyNamingConventions = false)]
    public string Batchable { get; set; }

    [YamlMember(Alias = ExcludedAssetsElement, ApplyNamingConventions = false)]
    public List<string> ExcludedAssets { get; set; }

    [YamlMember(Alias = MergePolicyElement, ApplyNamingConventions = false)]
    public List<MergePolicyYaml> MergePolicies { get; set; }

    [YamlMember(Alias = FailureNotificationTagsElement, ApplyNamingConventions = false)]
    public string FailureNotificationTags { get; set; }

    [DefaultValue("False")]
    [YamlMember(Alias = SourceEnabledElement, ApplyNamingConventions = false)]
    public string SourceEnabled { get; set; }

    [YamlMember(Alias = SourceDirectoryElement, ApplyNamingConventions = false)]
    public string SourceDirectory { get; set; }

    [YamlMember(Alias = TargetDirectoryElement, ApplyNamingConventions = false)]
    public string TargetDirectory { get; set; }
}
