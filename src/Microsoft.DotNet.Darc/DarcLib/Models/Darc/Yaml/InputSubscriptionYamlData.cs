// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.DarcLib.Models.Darc.Yaml;

/// <summary>
/// Helper class for YAML encoding/decoding purposes.
/// This is used so that we can have friendly alias names for elements.
/// </summary>
public class InputSubscriptionYamlData
{
    public const string SourceRepoElement = "Source Repository URL";
    public const string TargetRepoElement = "Target Repository URL";
    public const string SourceEnabledElement = "Source Enabled";

    private const string ChannelElement = "Channel";
    private const string TargetBranchElement = "Target Branch";
    private const string UpdateFrequencyElement = "Update Frequency";
    private const string MergePolicyElement = "Merge Policies";
    private const string BatchableElement = "Batchable";
    private const string FailureNotificationTagsElement = "Pull Request Failure Notification Tags";
    private const string SourceDirectoryElement = "Source Directory";
    private const string TargetDirectoryElement = "Target Directory";
    private const string ExcludedAssetsElement = "Excluded Assets";

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

    [YamlMember(Alias = BatchableElement, ApplyNamingConventions = false)]
    public string Batchable { get; set; }

    [YamlMember(Alias = ExcludedAssetsElement, ApplyNamingConventions = false)]
    public List<string> ExcludedAssets { get; set; }

    [YamlMember(Alias = MergePolicyElement, ApplyNamingConventions = false)]
    public List<MergePolicyYamlData> MergePolicies { get; set; }

    [YamlMember(Alias = FailureNotificationTagsElement, ApplyNamingConventions = false)]
    public string FailureNotificationTags { get; set; }

    [YamlMember(Alias = SourceEnabledElement, ApplyNamingConventions = false)]
    public string SourceEnabled { get; set; }

    [YamlMember(Alias = SourceDirectoryElement, ApplyNamingConventions = false)]
    public string SourceDirectory { get; set; }

    [YamlMember(Alias = TargetDirectoryElement, ApplyNamingConventions = false)]
    public string TargetDirectory { get; set; }
}

public class FullSubscriptionYamlData : InputSubscriptionYamlData
{
    [YamlMember(Alias = "Id", ApplyNamingConventions = false)]
    public Guid Id { get; set; }
}
