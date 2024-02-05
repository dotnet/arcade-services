// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.Darc.Models.PopUps;

public class UpdateSubscriptionPopUp : SubscriptionPopUp
{
    private readonly ILogger _logger;

    private readonly SubscriptionUpdateData _yamlData;

    public string Channel => _yamlData.Channel;

    public string SourceRepository => _yamlData.SourceRepository;

    public bool Batchable => bool.Parse(_yamlData.Batchable);

    public string UpdateFrequency => _yamlData.UpdateFrequency;

    public bool Enabled => bool.Parse(_yamlData.Enabled);

    public string FailureNotificationTags => _yamlData.FailureNotificationTags;

    public List<MergePolicy> MergePolicies => MergePoliciesPopUpHelpers.ConvertMergePolicies(_yamlData.MergePolicies);

    public bool SourceEnabled => bool.Parse(_yamlData.SourceEnabled);

    public IReadOnlyCollection<string> ExcludedAssets => _yamlData.ExcludedAssets;

    public UpdateSubscriptionPopUp(
        string path,
        ILogger logger,
        Subscription subscription,
        IEnumerable<string> suggestedChannels,
        IEnumerable<string> suggestedRepositories,
        IEnumerable<string> availableUpdateFrequencies,
        IEnumerable<string> availableMergePolicyHelp,
        string failureNotificationTags,
        bool? sourceEnabled,
        List<string> excludedAssets)
        : base(path, suggestedChannels, suggestedRepositories, availableMergePolicyHelp)
    {
        _logger = logger;

        _yamlData = new SubscriptionUpdateData
        {
            Id = GetCurrentSettingForDisplay(subscription.Id.ToString(), subscription.Id.ToString(), false),
            Channel = GetCurrentSettingForDisplay(subscription.Channel.Name, subscription.Channel.Name, false),
            SourceRepository = GetCurrentSettingForDisplay(subscription.SourceRepository, subscription.SourceRepository, false),
            Batchable = GetCurrentSettingForDisplay(subscription.Policy.Batchable.ToString(), subscription.Policy.Batchable.ToString(), false),
            UpdateFrequency = GetCurrentSettingForDisplay(subscription.Policy.UpdateFrequency.ToString(), subscription.Policy.UpdateFrequency.ToString(), false),
            Enabled = GetCurrentSettingForDisplay(subscription.Enabled.ToString(), subscription.Enabled.ToString(), false),
            FailureNotificationTags = GetCurrentSettingForDisplay(failureNotificationTags, failureNotificationTags, false),
            MergePolicies = MergePoliciesPopUpHelpers.ConvertMergePolicies(subscription.Policy.MergePolicies),
            SourceEnabled = GetCurrentSettingForDisplay(sourceEnabled?.ToString(), false.ToString(), false),
            ExcludedAssets = excludedAssets,
        };

        ISerializer serializer = new SerializerBuilder().Build();

        string yaml = serializer.Serialize(_yamlData);

        string[] lines = yaml.Split(Environment.NewLine);

        // Initialize line contents.  Augment the input lines with suggestions and explanation
        Contents = new Collection<Line>(new List<Line>
        {
            new($"Use this form to update the values of subscription '{subscription.Id}'.", true),
            new($"Note that if you are setting 'Is batchable' to true you need to remove all Merge Policies.", true),
            new()
        });

        foreach (string line in lines)
        {
            Contents.Add(new Line(line));
        }

        PrintSuggestions();
    }

    public override int ProcessContents(IList<Line> contents)
    {
        SubscriptionUpdateData outputYamlData;

        try
        {
            string yamlString = contents.Aggregate("", (current, line) => $"{current}{Environment.NewLine}{line.Text}");
            IDeserializer serializer = new DeserializerBuilder().Build();
            outputYamlData = serializer.Deserialize<SubscriptionUpdateData>(yamlString);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to parse input yaml.  Please see help for correct format.");
            return Constants.ErrorCode;
        }

        _yamlData.Batchable = ParseSetting(outputYamlData.Batchable, _yamlData.Batchable, false);
        _yamlData.Enabled = ParseSetting(outputYamlData.Enabled, _yamlData.Enabled, false);
        // Make sure Batchable and Enabled are valid bools
        if (!bool.TryParse(outputYamlData.Batchable, out bool batchable) || !bool.TryParse(outputYamlData.Enabled, out bool enabled))
        {
            _logger.LogError("Either Batchable or Enabled is not a valid boolean values.");
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

        _yamlData.UpdateFrequency = ParseSetting(outputYamlData.UpdateFrequency, _yamlData.UpdateFrequency, false);
        if (string.IsNullOrEmpty(_yamlData.UpdateFrequency) || 
            !Constants.AvailableFrequencies.Contains(_yamlData.UpdateFrequency, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogError($"Frequency should be provided and should be one of the following: " +
                             $"'{string.Join("', '",Constants.AvailableFrequencies)}'");
            return Constants.ErrorCode;
        }

        _yamlData.FailureNotificationTags = ParseSetting(outputYamlData.FailureNotificationTags, _yamlData.FailureNotificationTags, false);

        return Constants.SuccessCode;
    }

    /// <summary>
    /// Helper class for YAML encoding/decoding purposes.
    /// This is used so that we can have friendly alias names for elements.
    /// </summary>
    private class SubscriptionUpdateData : SubscriptionData
    {
        public const string EnabledElement = "Enabled";

        [YamlMember(ApplyNamingConventions = false)]
        public string Id { get; set; }

        [YamlMember(Alias = EnabledElement, ApplyNamingConventions = false)]
        public string Enabled { get; set; }
    }
}
