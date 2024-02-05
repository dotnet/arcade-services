// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.Darc.Models.PopUps;

/// <summary>
/// Common class for subscription management popups.
/// </summary>
public abstract class SubscriptionPopUp : EditorPopUp
{
    protected const string ChannelElement = "Channel";
    protected const string SourceRepoElement = "Source Repository URL";
    protected const string TargetRepoElement = "Target Repository URL";
    protected const string TargetBranchElement = "Target Branch";
    protected const string UpdateFrequencyElement = "Update Frequency";
    protected const string MergePolicyElement = "Merge Policies";
    protected const string BatchableElement = "Batchable";
    protected const string FailureNotificationTagsElement = "Pull Request Failure Notification Tags";
    protected const string SourceEnabledElement = "SourceEnabled";
    protected const string ExcludedAssetsElement = "ExcludedAssets";

    private readonly IEnumerable<string> _suggestedChannels;
    private readonly IEnumerable<string> _suggestedRepositories;
    private readonly IEnumerable<string> _availableMergePolicyHelp;

    protected SubscriptionPopUp(
        string path,
        IEnumerable<string> suggestedChannels,
        IEnumerable<string> suggestedRepositories,
        IEnumerable<string> availableMergePolicyHelp)
        : base(path)
    {
        _suggestedChannels = suggestedChannels;
        _suggestedRepositories = suggestedRepositories;
        _availableMergePolicyHelp = availableMergePolicyHelp;
    }

    /// <summary>
    /// Prints a section at the end that gives examples on usage
    /// </summary>
    protected void PrintSuggestions()
    {
        Contents.Add(new Line($"Suggested repository URLs for '{SourceRepoElement}' or '{TargetRepoElement}':", true));

        foreach (string suggestedRepo in _suggestedRepositories)
        {
            Contents.Add(new Line($"  {suggestedRepo}", true));
        }

        Contents.Add(new Line("", true));
        Contents.Add(new Line("Suggested Channels", true));

        foreach (string suggestedChannel in _suggestedChannels)
        {
            Contents.Add(new Line($"  {suggestedChannel}", true));
        }

        Contents.Add(new Line("", true));
        Contents.Add(new Line("Available Merge Policies", true));

        foreach (string mergeHelp in _availableMergePolicyHelp)
        {
            Contents.Add(new Line($"  {mergeHelp}", true));
        }

        Contents.Add(new Line("", true));
        Contents.Add(new Line("Examples of excluded assets", true));
        Contents.Add(new Line($"  - Microsoft.DotNet.Arcade.Sdk", true));
        Contents.Add(new Line($"  - Microsoft.Extensions.*", true));
    }

    /// <summary>
    /// Helper class for YAML encoding/decoding purposes.
    /// This is used so that we can have friendly alias names for elements.
    /// </summary>
    protected class SubscriptionData
    {
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

        [YamlMember(Alias = MergePolicyElement, ApplyNamingConventions = false)]
        public List<MergePolicyData> MergePolicies { get; set; }

        [YamlMember(Alias = FailureNotificationTagsElement, ApplyNamingConventions = false)]
        public string FailureNotificationTags { get; set; }

        [YamlMember(Alias = BatchableElement, ApplyNamingConventions = false)]
        public string SourceEnabled { get; set; }

        [YamlMember(Alias = ExcludedAssetsElement, ApplyNamingConventions = false)]
        public List<string> ExcludedAssets { get; set; }
    }
}
