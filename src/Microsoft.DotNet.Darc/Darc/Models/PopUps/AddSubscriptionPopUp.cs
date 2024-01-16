// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.Darc.Models.PopUps;

public class AddSubscriptionPopUp : EditorPopUp
{
    private readonly ILogger _logger;
    private readonly SubscriptionData _yamlData;
    public string Channel => _yamlData.Channel;
    public string SourceRepository => _yamlData.SourceRepository;
    public string TargetRepository => _yamlData.TargetRepository;
    public string TargetBranch => _yamlData.TargetBranch;
    public string UpdateFrequency => _yamlData.UpdateFrequency;
    public List<MergePolicy> MergePolicies => MergePoliciesPopUpHelpers.ConvertMergePolicies(_yamlData.MergePolicies);
    public bool Batchable => bool.Parse(_yamlData.Batchable);
    public string FailureNotificationTags => _yamlData.FailureNotificationTags;

    public AddSubscriptionPopUp(string path,
        ILogger logger,
        string channel,
        string sourceRepository,
        string targetRepository,
        string targetBranch,
        string updateFrequency,
        bool batchable,
        List<MergePolicy> mergePolicies,
        IEnumerable<string> suggestedChannels,
        IEnumerable<string> suggestedRepositories,
        IEnumerable<string> availableUpdateFrequencies,
        IEnumerable<string> availableMergePolicyHelp,
        string failureNotificationTags)
        : base(path)
    {
        _logger = logger;
        _yamlData = new SubscriptionData
        {
            Channel = GetCurrentSettingForDisplay(channel, "<required>", false),
            SourceRepository = GetCurrentSettingForDisplay(sourceRepository, "<required>", false),
            TargetRepository = GetCurrentSettingForDisplay(targetRepository, "<required>", false),
            TargetBranch = GetCurrentSettingForDisplay(targetBranch, "<required>", false),
            UpdateFrequency = GetCurrentSettingForDisplay(updateFrequency, $"<'{string.Join("', '", Constants.AvailableFrequencies)}'>", false),
            Batchable = GetCurrentSettingForDisplay(batchable.ToString(), batchable.ToString(), false),
            MergePolicies = MergePoliciesPopUpHelpers.ConvertMergePolicies(mergePolicies),
            FailureNotificationTags = failureNotificationTags
        };

        ISerializer serializer = new SerializerBuilder().Build();
        string yaml = serializer.Serialize(_yamlData);
        string[] lines = yaml.Split(Environment.NewLine);

        // Initialize line contents.  Augment the input lines with suggestions and explanation
        Contents = new Collection<Line>(new List<Line>
        {
            new("Use this form to create a new subscription.", true),
            new("A subscription maps a build of a source repository that has been applied to a specific channel", true),
            new("onto a specific branch in a target repository.  The subscription has a trigger (update frequency)", true),
            new("and merge policy. If a subscription is batchable, no merge policy should be provided, and the", true),
            new("set-repository-policies command should be used instead to set policies at the repository and branch level. ", true),
            new("For non-batched subscriptions, providing a list of semicolon-delineated GitHub tags will tag these", true),
            new("logins when monitoring the pull requests, once one or more policy checks fail.", true),
            new("For additional information about subscriptions, please see", true),
            new("https://github.com/dotnet/arcade/blob/main/Documentation/BranchesChannelsAndSubscriptions.md", true),
            new("", true),
            new("Fill out the following form.  Suggested values for fields are shown below.", true),
            new()
        });
        foreach (string line in lines)
        {
            Contents.Add(new Line(line));
        }
        // Add helper comments
        Contents.Add(new Line($"Suggested repository URLs for '{SubscriptionData.SourceRepoElement}' or '{SubscriptionData.TargetRepoElement}':", true));
        foreach (string suggestedRepo in suggestedRepositories) {
            Contents.Add(new Line($"  {suggestedRepo}", true));
        }
        Contents.Add(new Line("", true));
        Contents.Add(new Line("Suggested Channels", true));
        foreach (string suggestedChannel in suggestedChannels)
        {
            Contents.Add(new Line($"  {suggestedChannel}", true));
        }
        Contents.Add(new Line("", true));
        Contents.Add(new Line("Available Merge Policies", true));
        foreach (string mergeHelp in availableMergePolicyHelp)
        {
            Contents.Add(new Line($"  {mergeHelp}", true));
        }
    }

    public override int ProcessContents(IList<Line> contents)
    {
        SubscriptionData outputYamlData;

        try
        {
            // Join the lines back into a string and deserialize as YAML.
            // TODO: Alter the popup/ux manager to pass along the raw file to avoid this unnecessary
            // operation once authenticate ends up as YAML.
            string yamlString = contents.Aggregate("", (current, line) => $"{current}{System.Environment.NewLine}{line.Text}");
            IDeserializer serializer = new DeserializerBuilder().Build();
            outputYamlData = serializer.Deserialize<SubscriptionData>(yamlString);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to parse input yaml.  Please see help for correct format.");
            return Constants.ErrorCode;
        }

        // Validate the merge policies
        if (!MergePoliciesPopUpHelpers.ValidateMergePolicies(MergePoliciesPopUpHelpers.ConvertMergePolicies(outputYamlData.MergePolicies), _logger))
        {
            return Constants.ErrorCode;
        }

        _yamlData.MergePolicies = outputYamlData.MergePolicies;

        // Parse and check the input fields
        _yamlData.Channel = ParseSetting(outputYamlData.Channel, _yamlData.Channel, false);
        if (string.IsNullOrEmpty(_yamlData.Channel))
        {
            _logger.LogError("Channel must be non-empty");
            return Constants.ErrorCode;
        }

        _yamlData.SourceRepository = ParseSetting(outputYamlData.SourceRepository, _yamlData.SourceRepository, false);
        if (string.IsNullOrEmpty(_yamlData.SourceRepository))
        {
            _logger.LogError("Source repository URL must be non-empty");
            return Constants.ErrorCode;
        }

        _yamlData.TargetRepository = ParseSetting(outputYamlData.TargetRepository, _yamlData.TargetRepository, false);
        if (string.IsNullOrEmpty(_yamlData.TargetRepository))
        {
            _logger.LogError("Target repository URL must be non-empty");
            return Constants.ErrorCode;
        }

        _yamlData.TargetBranch = ParseSetting(outputYamlData.TargetBranch, _yamlData.TargetBranch, false);
        if (string.IsNullOrEmpty(_yamlData.TargetBranch))
        {
            _logger.LogError("Target branch must be non-empty");
            return Constants.ErrorCode;
        }

        _yamlData.Batchable = outputYamlData.Batchable;

        _yamlData.FailureNotificationTags = outputYamlData.FailureNotificationTags;

        _yamlData.UpdateFrequency = ParseSetting(outputYamlData.UpdateFrequency, _yamlData.UpdateFrequency, false);
        if (string.IsNullOrEmpty(_yamlData.UpdateFrequency) ||
            !Constants.AvailableFrequencies.Contains(_yamlData.UpdateFrequency, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogError($"Frequency should be provided and should be one of the following: " +
                             $"'{string.Join("', '",Constants.AvailableFrequencies)}'");
            return Constants.ErrorCode;
        }

        return Constants.SuccessCode;
    }

    /// <summary>
    /// Helper class for YAML encoding/decoding purposes.
    /// This is used so that we can have friendly alias names for elements.
    /// </summary>
    private class SubscriptionData
    {
        public const string ChannelElement = "Channel";
        public const string SourceRepoElement = "Source Repository URL";
        public const string TargetRepoElement = "Target Repository URL";
        public const string TargetBranchElement = "Target Branch";
        public const string UpdateFrequencyElement = "Update Frequency";
        public const string MergePolicyElement = "Merge Policies";
        public const string BatchableElement = "Batchable";
        public const string FailureNotificationTagsElement = "Pull Request Failure Notification Tags";

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
    }
}
