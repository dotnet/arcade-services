﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.Darc.Models.PopUps;

/// <summary>
/// Common class for subscription management popups.
/// </summary>
public abstract class SubscriptionPopUp : EditorPopUp
{
    private const string ChannelElement = "Channel";
    private const string SourceRepoElement = "Source Repository URL";
    private const string TargetRepoElement = "Target Repository URL";
    private const string TargetBranchElement = "Target Branch";
    private const string UpdateFrequencyElement = "Update Frequency";
    private const string MergePolicyElement = "Merge Policies";
    private const string BatchableElement = "Batchable";
    private const string FailureNotificationTagsElement = "Pull Request Failure Notification Tags";
    private const string SourceEnabledElement = "SourceEnabled";
    private const string ExcludedAssetsElement = "ExcludedAssets";

    protected readonly SubscriptionData _data;
    private readonly IEnumerable<string> _suggestedChannels;
    private readonly IEnumerable<string> _suggestedRepositories;
    private readonly IEnumerable<string> _availableMergePolicyHelp;
    private readonly ILogger _logger;

    public string Channel => _data.Channel;
    public string SourceRepository => _data.SourceRepository;
    public string TargetRepository => _data.TargetRepository;
    public string TargetBranch => _data.TargetBranch;
    public string UpdateFrequency => _data.UpdateFrequency;
    public List<MergePolicy> MergePolicies => MergePoliciesPopUpHelpers.ConvertMergePolicies(_data.MergePolicies);
    public bool Batchable => bool.Parse(_data.Batchable);
    public string FailureNotificationTags => _data.FailureNotificationTags;
    public bool SourceEnabled => bool.Parse(_data.SourceEnabled);
    public IReadOnlyCollection<string> ExcludedAssets => _data.ExcludedAssets;

    protected SubscriptionPopUp(
        string path,
        IEnumerable<string> suggestedChannels,
        IEnumerable<string> suggestedRepositories,
        IEnumerable<string> availableMergePolicyHelp,
        ILogger logger,
        SubscriptionData data)
        : base(path)
    {
        _data = data;
        _suggestedChannels = suggestedChannels;
        _suggestedRepositories = suggestedRepositories;
        _availableMergePolicyHelp = availableMergePolicyHelp;
        _logger = logger;
    }

    protected int ParseAndValidateData(SubscriptionData outputYamlData)
    {
        if (!MergePoliciesPopUpHelpers.ValidateMergePolicies(MergePoliciesPopUpHelpers.ConvertMergePolicies(outputYamlData.MergePolicies), _logger))
        {
            return Constants.ErrorCode;
        }

        _data.MergePolicies = outputYamlData.MergePolicies;

        _data.Channel = ParseSetting(outputYamlData.Channel, _data.Channel, false);
        if (string.IsNullOrEmpty(_data.Channel))
        {
            _logger.LogError("Channel must be non-empty");
            return Constants.ErrorCode;
        }

        _data.SourceRepository = ParseSetting(outputYamlData.SourceRepository, _data.SourceRepository, false);
        if (string.IsNullOrEmpty(_data.SourceRepository))
        {
            _logger.LogError("Source repository URL must be non-empty");
            return Constants.ErrorCode;
        }

        _data.Batchable = ParseSetting(outputYamlData.Batchable, _data.Batchable, false);

        if (!bool.TryParse(outputYamlData.Batchable, out bool _))
        {
            _logger.LogError("Batchable is not a valid boolean value.");
            return Constants.ErrorCode;
        }

        _data.UpdateFrequency = ParseSetting(outputYamlData.UpdateFrequency, _data.UpdateFrequency, false);
        if (string.IsNullOrEmpty(_data.UpdateFrequency) ||
            !Constants.AvailableFrequencies.Contains(_data.UpdateFrequency, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogError($"Frequency should be provided and should be one of the following: " +
                             $"'{string.Join("', '", Constants.AvailableFrequencies)}'");
            return Constants.ErrorCode;
        }

        _data.SourceEnabled = outputYamlData.SourceEnabled;

        if (!bool.TryParse(outputYamlData.SourceEnabled, out bool sourceEnabled))
        {
            _logger.LogError("SourceEnabled is not a valid boolean value.");
            return Constants.ErrorCode;
        }

        if (outputYamlData.ExcludedAssets.Any() && !sourceEnabled)
        {
            Console.WriteLine("Asset exclusion only works for source-enabled subscriptions");
            return Constants.ErrorCode;
        }

        _data.FailureNotificationTags = ParseSetting(outputYamlData.FailureNotificationTags, _data.FailureNotificationTags, false);
        _data.ExcludedAssets = outputYamlData.ExcludedAssets;

        return Constants.SuccessCode;
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
        Contents.Add(new Line("Excluded assets only apply to source-enabled subscription (VMR code flow subscriptions).", true));
        Contents.Add(new Line("They can contain * to ignore whole groups of assets.", true));
        Contents.Add(new Line("Examples of excluded assets:", true));
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

        [YamlMember(Alias = SourceEnabledElement, ApplyNamingConventions = false)]
        public string SourceEnabled { get; set; }

        [YamlMember(Alias = ExcludedAssetsElement, ApplyNamingConventions = false)]
        public List<string> ExcludedAssets { get; set; }
    }
}
